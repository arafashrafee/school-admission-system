import type { HoldStatus } from "../types";
const labels: Record<HoldStatus, string> = {
  HELD: "Seat held",
  CONFIRMED: "Confirmed",
  EXPIRED: "Expired",
  CANCELLED: "Cancelled",
};
export function StatusBadge({ status }: { status: HoldStatus }) {
  return (
    <span className={`badge status-${status.toLowerCase()}`}>
      <span className="status-dot" />
      {labels[status]}
    </span>
  );
}
