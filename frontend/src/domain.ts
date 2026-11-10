import type { HoldStatus } from "./types";
export const isStudentId = (value: string): boolean =>
  value.length === 17 && /^[0-9]{17}$/.test(value);
export const remainingSeconds = (expiresAt: string, now = Date.now()): number =>
  Math.max(0, Math.ceil((Date.parse(expiresAt) - now) / 1000));
export const effectiveStatus = (
  status: HoldStatus,
  expiresAt: string,
  now = Date.now(),
): HoldStatus =>
  status === "HELD" && remainingSeconds(expiresAt, now) === 0
    ? "EXPIRED"
    : status;
export const formatCountdown = (seconds: number): string =>
  `${Math.floor(seconds / 60)
    .toString()
    .padStart(2, "0")}:${(seconds % 60).toString().padStart(2, "0")}`;
export const dateLabel = (value: string): string =>
  new Intl.DateTimeFormat("en-BD", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
