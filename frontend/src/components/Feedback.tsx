import { AlertCircle, CheckCircle2, LoaderCircle } from "lucide-react";
export function Feedback({
  message,
  kind = "error",
  retry,
}: {
  message: string;
  kind?: "error" | "success" | "info";
  retry?: () => void;
}) {
  return (
    <div
      className={`feedback ${kind}`}
      role={kind === "error" ? "alert" : "status"}
    >
      {kind === "success" ? (
        <CheckCircle2 size={19} />
      ) : (
        <AlertCircle size={19} />
      )}
      <span>{message}</span>
      {retry && (
        <button className="text-button" onClick={retry}>
          Try again
        </button>
      )}
    </div>
  );
}
export function Loading({ label }: { label: string }) {
  return (
    <div className="empty" role="status">
      <LoaderCircle className="spin" size={24} />
      <p>{label}</p>
    </div>
  );
}
