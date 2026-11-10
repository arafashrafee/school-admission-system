import { useState } from "react";
import { Fingerprint, ArrowRight, ShieldAlert } from "lucide-react";
import { isStudentId } from "../domain";
export function IdentityPanel({
  studentId,
  onChange,
}: {
  studentId: string | null;
  onChange: (id: string | null) => void;
}) {
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState("");
  const [error, setError] = useState("");
  return (
    <aside className="identity-panel panel">
      <div className="icon-tile">
        <Fingerprint size={25} />
      </div>
      <h2>Your student identity</h2>
      {studentId && !editing ? (
        <>
          <p className="small muted">Birth registration number</p>
          <p className="identity-number">{studentId}</p>
          <p className="small">Your identity is saved on this browser.</p>
          <div className="identity-actions">
            <button
              className="text-button"
              onClick={() => {
                setEditing(true);
                setValue("");
                setError("");
              }}
            >
              Switch student
            </button>
            <button className="text-button" onClick={() => onChange(null)}>
              Clear identity
            </button>
          </div>
        </>
      ) : (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            if (!isStudentId(value)) {
              setError("Enter exactly 17 digits. Keep any leading zeroes.");
              return;
            }
            onChange(value);
            setEditing(false);
            setValue("");
            setError("");
          }}
        >
          <p className="muted small">
            Use your 17-digit Bangladesh birth registration number to manage
            your applications.
          </p>
          <label htmlFor="student-id">Birth registration number</label>
          <input
            id="student-id"
            inputMode="numeric"
            autoComplete="off"
            spellCheck={false}
            value={value}
            onChange={(event) => {
              setValue(event.target.value);
              setError("");
            }}
            aria-invalid={!!error}
            aria-describedby="identity-help identity-error"
            placeholder="17-digit number"
          />
          <p id="identity-help" className="field-help">
            Use English digits (0–9), including leading zeroes.
          </p>
          {error && (
            <p id="identity-error" className="field-error" role="alert">
              {error}
            </p>
          )}
          <button className="button primary wide" type="submit">
            {studentId ? "Switch identity" : "Continue as student"}
            <ArrowRight size={16} />
          </button>
          {studentId && (
            <button
              className="text-button"
              type="button"
              onClick={() => setEditing(false)}
            >
              Keep current identity
            </button>
          )}
        </form>
      )}
      <div className="privacy-note">
        <ShieldAlert size={17} />
        <p>
          Identifier-based demo access, not secure authentication. Anyone with
          this number can access its applications. Clear your identity on shared
          devices.
        </p>
      </div>
    </aside>
  );
}
