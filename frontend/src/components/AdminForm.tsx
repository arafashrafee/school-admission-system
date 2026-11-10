import { useState } from "react";
import { Plus } from "lucide-react";
export function AdminForm({
  busy,
  onCreate,
}: {
  busy: boolean;
  onCreate: (name: string, seats: number) => Promise<boolean>;
}) {
  const [name, setName] = useState("");
  const [seats, setSeats] = useState("");
  const [error, setError] = useState("");
  return (
    <section className="admin-form panel">
      <h2>Create a class offering</h2>
      <p className="muted small">
        Include class and shift in one name, such as “Class 3 · Morning”.
      </p>
      <form
        onSubmit={async (event) => {
          event.preventDefault();
          const total = Number(seats);
          if (
            !name.trim() ||
            name.trim().length > 100 ||
            !Number.isInteger(total) ||
            total <= 0 ||
            total > 2147483647
          ) {
            setError(
              "Enter a name of 1–100 characters and a positive whole number of seats.",
            );
            return;
          }
          setError("");
          if (await onCreate(name.trim(), total)) {
            setName("");
            setSeats("");
          }
        }}
      >
        <div className="form-grid">
          <div>
            <label htmlFor="class-name">Class & shift name</label>
            <input
              id="class-name"
              required
              maxLength={100}
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder="e.g. Class 3 · Morning"
            />
          </div>
          <div>
            <label htmlFor="total-seats">Total seats</label>
            <input
              id="total-seats"
              required
              type="number"
              min={1}
              max={2147483647}
              step={1}
              value={seats}
              onChange={(event) => setSeats(event.target.value)}
              placeholder="e.g. 30"
            />
          </div>
        </div>
        {error && (
          <p className="field-error" role="alert">
            {error}
          </p>
        )}
        <button className="button primary" disabled={busy} type="submit">
          <Plus size={17} />
          {busy ? "Creating offering…" : "Create class offering"}
        </button>
      </form>
    </section>
  );
}
