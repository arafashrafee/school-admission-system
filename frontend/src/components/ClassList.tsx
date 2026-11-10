import { ArrowUpRight, BookOpen, Users } from "lucide-react";
import type { ClassOffering, StudentHold } from "../types";
import { effectiveStatus } from "../domain";
export function ClassList({
  classes,
  holds,
  studentId,
  busy,
  onApply,
  admin = false,
}: {
  classes: ClassOffering[];
  holds: StudentHold[];
  studentId: string | null;
  busy: string | null;
  onApply: (offering: ClassOffering) => void;
  admin?: boolean;
}) {
  if (!classes.length)
    return (
      <div className="empty">
        <BookOpen />
        <h3>No class offerings yet</h3>
        <p>
          Class offerings will appear here when they are added by an
          administrator.
        </p>
      </div>
    );
  return (
    <div className="class-grid">
      {classes.map((offering) => {
        const existing = holds.some(
          (h) =>
            h.classId === offering.id &&
            ["HELD", "CONFIRMED"].includes(
              effectiveStatus(h.status, h.expiresAt),
            ),
        );
        const full = offering.availableSeats === 0;
        return (
          <article className="class-card" key={offering.id}>
            <div className="card-top">
              <span className="book-icon">
                <BookOpen size={21} />
              </span>
              <span
                className={`badge ${full ? "status-expired" : "available"}`}
              >
                {full ? "Full" : "Available"}
              </span>
            </div>
            <h3>{offering.name}</h3>
            <p className="muted small">Class & shift offering</p>
            <div className="seat-highlight">
              <strong>{offering.availableSeats}</strong>
              <span>
                seats available{" "}
                <span className="muted">of {offering.totalSeats}</span>
              </span>
            </div>
            <div
              className="capacity-track"
              role="img"
              aria-label={`${offering.availableSeats} of ${offering.totalSeats} seats available`}
            >
              <span
                style={{
                  width: `${Math.min(100, ((offering.totalSeats - offering.availableSeats) / offering.totalSeats) * 100)}%`,
                }}
              />
            </div>
            <dl className="seat-breakdown">
              <div>
                <dt>Held</dt>
                <dd>{offering.heldSeats}</dd>
              </div>
              <div>
                <dt>Confirmed</dt>
                <dd>{offering.confirmedSeats}</dd>
              </div>
              <div>
                <dt>Total seats</dt>
                <dd>{offering.totalSeats}</dd>
              </div>
            </dl>
            {admin ? (
              <p className="card-foot small muted">
                <Users size={15} /> Live seat summary
              </p>
            ) : (
              <button
                className="button primary wide"
                disabled={!!busy || full || existing || !studentId}
                onClick={() => onApply(offering)}
              >
                {busy === `apply:${offering.id}`
                  ? "Reserving seat…"
                  : existing
                    ? "Already applied"
                    : full
                      ? "No seats available"
                      : !studentId
                        ? "Enter student identity to apply"
                        : "Apply for a seat"}
                {!full && !existing && studentId && <ArrowUpRight size={17} />}
              </button>
            )}
          </article>
        );
      })}
    </div>
  );
}
