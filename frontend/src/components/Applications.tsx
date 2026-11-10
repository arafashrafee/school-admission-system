import { useEffect, useRef, useState } from "react";
import { Clock3, FileCheck2, CreditCard, X } from "lucide-react";
import type { StudentHold } from "../types";
import {
  effectiveStatus,
  remainingSeconds,
  formatCountdown,
  dateLabel,
} from "../domain";
import { StatusBadge } from "./StatusBadge";
function ApplicationCard({
  hold,
  busy,
  onConfirm,
  onCancel,
  onExpire,
}: {
  hold: StudentHold;
  busy: string | null;
  onConfirm: (hold: StudentHold) => void;
  onCancel: (hold: StudentHold) => void;
  onExpire: (hold: StudentHold) => void;
}) {
  const [now, setNow] = useState(Date.now);
  const notified = useRef(false);
  useEffect(() => {
    setNow(Date.now());
    if (hold.status !== "HELD") return;
    const interval = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(interval);
  }, [hold.status, hold.expiresAt]);
  const status = effectiveStatus(hold.status, hold.expiresAt, now);
  useEffect(() => {
    if (hold.status === "HELD" && status === "EXPIRED" && !notified.current) {
      notified.current = true;
      onExpire(hold);
    }
  }, [status, hold, onExpire]);
  return (
    <article className="application-card panel">
      <div className="application-heading">
        <div>
          <h3>{hold.className}</h3>
          <p className="small muted">
            Application{" "}
            <span className="reference">{hold.holdId.slice(0, 8)}</span>
          </p>
        </div>
        <StatusBadge status={status} />
      </div>
      {status === "HELD" ? (
        <>
          <div
            className={`countdown ${remainingSeconds(hold.expiresAt, now) <= 60 ? "urgent" : ""}`}
          >
            <Clock3 size={21} />
            <div>
              <span className="small">Seat reserved for</span>
              <strong role="timer" aria-label="Time remaining">
                {formatCountdown(remainingSeconds(hold.expiresAt, now))}
              </strong>
            </div>
            <span className="small muted">
              Expires {dateLabel(hold.expiresAt)}
            </span>
          </div>
          <p className="small muted">
            Confirm before the timer ends. Payment is simulated; no real money
            is charged.
          </p>
          <div className="application-actions">
            <button
              className="button primary"
              disabled={!!busy}
              onClick={() => onConfirm(hold)}
            >
              <CreditCard size={17} />
              {busy === `confirm:${hold.holdId}`
                ? "Confirming…"
                : "Confirm Admission"}
            </button>
            <button
              className="button secondary"
              disabled={!!busy}
              onClick={() => onCancel(hold)}
            >
              Cancel reservation
            </button>
          </div>
        </>
      ) : (
        <p className="application-message">
          {status === "CONFIRMED"
            ? "Your admission is confirmed. No further action is needed."
            : status === "EXPIRED"
              ? "This reservation expired. Apply again from Available classes if seats remain."
              : "This reservation was cancelled and the seat was released."}
        </p>
      )}
      {status !== "HELD" && (
        <p className="small muted">
          Reservation deadline: {dateLabel(hold.expiresAt)}
        </p>
      )}
    </article>
  );
}
export function Applications({
  holds,
  busy,
  onConfirm,
  onCancel,
  onExpire,
  onBrowse,
}: {
  holds: StudentHold[];
  busy: string | null;
  onConfirm: (hold: StudentHold) => void;
  onCancel: (hold: StudentHold) => void;
  onExpire: (hold: StudentHold) => void;
  onBrowse: () => void;
}) {
  const [cancelling, setCancelling] = useState<StudentHold | null>(null);
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    if (cancelling) dialog.current?.showModal();
    else dialog.current?.close();
  }, [cancelling]);
  return (
    <>
      {!holds.length ? (
        <div className="empty panel">
          <FileCheck2 size={32} />
          <h3>Your next chapter starts here</h3>
          <p>
            You have no applications yet. Explore class offerings and reserve an
            available seat.
          </p>
          <button className="button primary" onClick={onBrowse}>
            Browse available classes
          </button>
        </div>
      ) : (
        <div className="applications-list">
          {holds.map((hold) => (
            <ApplicationCard
              key={hold.holdId}
              hold={hold}
              busy={busy}
              onConfirm={onConfirm}
              onCancel={setCancelling}
              onExpire={onExpire}
            />
          ))}
        </div>
      )}
      <dialog
        ref={dialog}
        onCancel={() => setCancelling(null)}
        onClose={() => setCancelling(null)}
        aria-labelledby="cancel-title"
        aria-describedby="cancel-description"
      >
        <div className="dialog-heading">
          <h2 id="cancel-title">Cancel this reservation?</h2>
          <button
            className="icon-button"
            aria-label="Close cancellation dialog"
            onClick={() => setCancelling(null)}
          >
            <X size={20} />
          </button>
        </div>
        <p id="cancel-description">
          Your seat in {cancelling?.className} will be released. You can reapply
          later if seats are still available.
        </p>
        <div className="application-actions">
          <button
            className="button secondary"
            autoFocus
            onClick={() => setCancelling(null)}
          >
            Keep reservation
          </button>
          <button
            className="button danger"
            onClick={() => {
              if (cancelling) onCancel(cancelling);
              setCancelling(null);
            }}
          >
            Yes, cancel reservation
          </button>
        </div>
      </dialog>
    </>
  );
}
