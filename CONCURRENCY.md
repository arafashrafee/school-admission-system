# Concurrency and expiration

## Invariants

For each class, occupied seats (persisted HELD plus CONFIRMED records after an
expiration sweep) never exceed `TotalSeats`. A student can have at most one
HELD or CONFIRMED record in a class. EXPIRED/CANCELLED records permit reapplication.
Clock-expired HELD records are excluded from read-time availability even before
the periodic worker persists EXPIRED.

## Lock ordering and allocation

All three lifecycle mutations use a Read Committed PostgreSQL transaction and lock
the parent class row with `SELECT * FROM "AdmissionClasses" WHERE "Id" = @id FOR UPDATE`.
This serializes decisions within a class while allowing different classes to proceed
independently. There is no in-memory lock or client-side capacity authority.

Reservation performs, under that class lock:

1. Sweep HELD rows with `ExpiresAt <= UtcNow` to EXPIRED.
2. Reject an existing HELD/CONFIRMED application for the student/class.
3. Count HELD + CONFIRMED rows and reject if occupancy reaches `TotalSeats`.
4. Insert the new HELD record with an absolute UTC deadline and commit.

Confirmation/cancellation first look up the hold's class ID without tracking,
then acquire the same parent class lock before reading the hold entity. They do
not issue a separate explicit `SELECT FOR UPDATE` on the hold. Normal UPDATEs
obtain PostgreSQL row locks as required. Operations never acquire class locks
in different orders because each mutation belongs to a single class.

## Confirmation and cancellation

Both verify the owning student before state changes. Reconfirming CONFIRMED is a
successful no-op. Repeating cancellation of CANCELLED is also a successful no-op.
Confirmed admissions cannot be cancelled. Cancelled holds cannot be confirmed.
If a HELD record has expired by the server clock, the mutation persists EXPIRED
and commits that transition before returning the expiration error; it does not
roll back the expiration change. Successful confirmation sets `ConfirmedAt`.

## Background expiration

`HoldExpirationWorker` executes a conditional SQL update every 15 seconds:

```sql
UPDATE "AdmissionHolds"
SET "Status" = 'EXPIRED'
WHERE "Status" = 'HELD' AND "ExpiresAt" <= @utcNow;
```

The worker needs no API request. It locks qualifying hold rows through the UPDATE,
never acquires class locks, and does not expire CONFIRMED/CANCELLED rows. Allocation
also eagerly sweeps under the class lock. Read responses compute effective expiry
from UTC timestamps, so background persistence latency does not hide free seats.

## Database defenses

| Rule | Enforcement |
|---|---|
| Positive total seats | Check constraint |
| Unique exact class names | Unique index after service trimming |
| 17 ASCII-digit student ID | String column, service validation, database check |
| Four valid lifecycle states | Enum conversion and database check |
| One active student/class application | Partial unique index on class/student for HELD/CONFIRMED |
| Capacity | Database occupancy query and class-row transaction serialization |
| Hold/class integrity | Foreign key with restricted class deletion |

## Verification

The suite uses real PostgreSQL. Existing tests cover 100 concurrent service requests
for 10 seats, duplicate applications, expiration/reapplication, owner checks,
confirmation idempotency, cancellation, and confirm/cancel races. HTTP tests issue
another 100 concurrent requests for exactly 10 seats and require precisely 10 HTTP
201 responses and 90 HTTP 409 responses.

Phase 4 adds simultaneous confirm/cancel/expiration-sweep checks for an already
expired deadline, a near-expiry deadline, and a future deadline. They assert a single
terminal state, matching mutation outcomes, exact seat counts, and safe subsequent
allocation. Original assertions are retained. The live background worker and
persistence across container restarts are also verified using isolated QA records.
