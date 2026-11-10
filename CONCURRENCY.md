# Concurrency Architecture & Transaction Design

## 1. System Invariants & Mathematical Guarantees

The School Admission System enforces strict invariants across all concurrent seat reservations and confirmations:

1. **Strict Capacity Constraint:**
   $$\text{OccupiedSeats}(c) = \text{Count}(\text{Status} \in \{\text{HELD}, \text{CONFIRMED}\}) \le \text{TotalSeats}(c)$$
   The number of concurrently held and confirmed seats for any class $c$ can never exceed `TotalSeats`.

2. **Capacity Release via Expiration:**
   Holds where $\text{Status} = \text{HELD}$ and $\text{ExpiresAt} \le \text{UtcNow}$ are treated as inactive and transitioned to $\text{EXPIRED}$, immediately releasing held capacity back to the available pool.

3. **Student Uniqueness Constraint:**
   A student ($s$) can hold at most one active ($\text{HELD}$) or $\text{CONFIRMED}$ admission in a specific class ($c$) at any given moment:
   $$\text{Count}(c, s, \text{Status} \in \{\text{HELD}, \text{CONFIRMED}\}) \le 1$$
   Expired and cancelled holds ($\text{Status} \in \{\text{EXPIRED}, \text{CANCELLED}\}$) are excluded from this uniqueness constraint, guaranteeing that a student whose hold expired is never permanently blocked from reapplying.

4. **Confirmation Idempotency:**
   Confirming an already $\text{CONFIRMED}$ hold is idempotent and returns success without altering state or double-counting seats.

5. **Database-Authoritative Decisions:**
   Zero in-memory seat counters or cached state. All capacity decisions, locks, and status transitions occur inside database transactions on PostgreSQL.

---

## 2. Pessimistic Row-Level Locking Strategy

To achieve deterministic consistency under high concurrency without introducing distributed locks or Redis:

- **Lock Target:** The parent `AdmissionClass` row.
- **Lock Type:** PostgreSQL exclusive row-level lock via `SELECT ... FOR UPDATE`.
- **Lock Scope:** Limited to the specific `AdmissionClass` requested, allowing transactions across different classes to proceed in parallel without contention.

```
       Incoming Request (Hold / Apply)
                     │
                     ▼
       ┌───────────────────────────┐
       │ BEGIN TRANSACTION         │ (ReadCommitted)
       └─────────────┬─────────────┘
                     │
                     ▼
       ┌───────────────────────────┐
       │ SELECT * FROM             │
       │   "AdmissionClasses"      │  <-- Acquires exclusive row lock
       │ WHERE "Id" = @ClassId     │      Serializes concurrent requests for @ClassId
       │ FOR UPDATE;               │
       └─────────────┬─────────────┘
                     │
                     ▼
       ┌───────────────────────────┐
       │ UPDATE "AdmissionHolds"   │
       │ SET "Status" = 'EXPIRED'  │  <-- Eager transactional expiration sweep
       │ WHERE "ClassId" = @ClassId│      Releases overdue capacity immediately
       │   AND "Status" = 'HELD'   │
       │   AND "ExpiresAt" <= @Now │
       └─────────────┬─────────────┘
                     │
                     ▼
       ┌───────────────────────────┐
       │ Check Duplicate Student   │
       │ Active Hold / Confirmed   │  <-- Rejects if student already has active seat
       └─────────────┬─────────────┘
                     │ (No duplicate)
                     ▼
       ┌───────────────────────────┐
       │ Calculate Occupied Seats  │
       │ COUNT(HELD + CONFIRMED)   │  <-- Atomic count from locked DB state
       └─────────────┬─────────────┘
                     │
          ┌──────────┴──────────┐
          │ Occupied < Total?   │
          └───┬─────────────┬───┘
              │ Yes         │ No
              ▼             ▼
       ┌──────────────┐   ┌───────────────────────┐
       │ INSERT Hold  │   │ ROLLBACK & Reject     │
       │ (Status=HELD)│   │ ("Class is full")     │
       └──────┬───────┘   └───────────────────────┘
              │
              ▼
       ┌──────────────┐
       │ COMMIT       │  <-- Releases row lock
       └──────────────┘
```

---

## 3. Transaction Execution Ordering

### Consistent Lock Hierarchy (Deadlock Prevention)
All transactional lifecycle operations (`ReserveSeatAsync`, `ConfirmHoldAsync`, `CancelHoldAsync`) acquire an exclusive lock on the parent `AdmissionClass` row **before** reading or mutating `AdmissionHold` rows:
1. `GetHoldClassIdAsync(holdId)` via non-tracking projection (no entity tracking).
2. `GetClassWithLockAsync(classId)` executes `SELECT ... FOR UPDATE` on `AdmissionClasses`.
3. Read / update `AdmissionHold` entities under active parent class lock.
This strictly uniform lock acquisition hierarchy eliminates cyclic dependency deadlocks between concurrent hold allocations, confirmations, and cancellations.

### A. Seat Hold Allocation Workflow
1. **Begin Transaction** with isolation level `ReadCommitted`.
2. **Execute Row Lock:**
   ```sql
   SELECT "Id", "Name", "TotalSeats"
   FROM "AdmissionClasses"
   WHERE "Id" = @ClassId
   FOR UPDATE;
   ```
   *If class not found: Rollback & return 404.*
3. **Execute Transactional Expiration Sweep:**
   ```sql
   UPDATE "AdmissionHolds"
   SET "Status" = 'EXPIRED'
   WHERE "ClassId" = @ClassId
     AND "Status" = 'HELD'
     AND "ExpiresAt" <= @UtcNow;
   ```
4. **Check Existing Active Student Admission:**
   ```sql
   SELECT EXISTS(
     SELECT 1 FROM "AdmissionHolds"
     WHERE "ClassId" = @ClassId
       AND "StudentId" = @StudentId
       AND "Status" IN ('HELD', 'CONFIRMED')
   );
   ```
   *If exists: Rollback & reject duplicate application.*
5. **Compute Authoritative Occupancy:**
   ```sql
   SELECT COUNT(*)
   FROM "AdmissionHolds"
   WHERE "ClassId" = @ClassId
     AND "Status" IN ('HELD', 'CONFIRMED');
   ```
6. **Evaluate Capacity Invariant:**
   If $\text{OccupiedCount} \ge \text{TotalSeats}$:
   - Rollback & return capacity exhausted error.
7. **Insert New Hold:**
   ```sql
   INSERT INTO "AdmissionHolds" (
     "Id", "ClassId", "StudentId", "Status", "CreatedAt", "ExpiresAt"
   ) VALUES (
     @NewId, @ClassId, @StudentId, 'HELD', @UtcNow, @ExpiresAt
   );
   ```
8. **Commit Transaction.**

---

### B. Seat Hold Confirmation Workflow
1. **Begin Transaction.**
2. **Execute Hold Row Lock:**
   ```sql
   SELECT *
   FROM "AdmissionHolds"
   WHERE "Id" = @HoldId
   FOR UPDATE;
   ```
   *If hold not found: Rollback & return 404.*
3. **Evaluate Hold Status:**
   - If `Status == 'CONFIRMED'`: Idempotent return (success, no changes).
   - If `Status == 'EXPIRED'` or `Status == 'CANCELLED'`: Rollback & return error ("Hold is no longer valid").
   - If `Status == 'HELD'` and `ExpiresAt <= @UtcNow`:
     Transition to `EXPIRED`, rollback, and return error ("Hold expired").
4. **Confirm Admission:**
   ```sql
   UPDATE "AdmissionHolds"
   SET "Status" = 'CONFIRMED',
       "ConfirmedAt" = @UtcNow
   WHERE "Id" = @HoldId;
   ```
5. **Commit Transaction.**

---

## 4. Multi-Layer Defense Against Invariant Violations

| Invariant | Database Constraint | Transactional Protection |
|---|---|---|
| **Class Total Seats Positive** | Check constraint: `"TotalSeats" > 0` | Entity validation |
| **Unique Class Name** | Unique index: `IX_AdmissionClasses_Name` | Database enforced |
| **Valid StudentId (17 Digits)** | Check constraint: `length("StudentId") = 17 AND "StudentId" ~ '^[0-9]{17}$'` | Model validation & DB check |
| **Valid Status Values** | Check constraint: `"Status" IN ('HELD', 'CONFIRMED', 'EXPIRED', 'CANCELLED')` | Strongly-typed C# enum |
| **Prevent Duplicate Active Student** | Unique partial index: `IX_AdmissionHolds_ClassId_StudentId_Active` | Pre-check under row lock + DB partial unique index |
| **Never Exceed TotalSeats** | Indexed occupancy query under row lock | `SELECT FOR UPDATE` serializes evaluations |
| **Expired Holds Release Capacity** | Partial index on `("Status", "ExpiresAt") WHERE "Status" = 'HELD'` | Eager transactional sweep + periodic background worker |
