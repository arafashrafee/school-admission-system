import { useCallback, useEffect, useRef, useState } from "react";
import {
  GraduationCap,
  BookOpen,
  FileCheck2,
  Settings2,
  RefreshCw,
  ArrowRight,
  CheckCircle2,
} from "lucide-react";
import { AdmissionApi } from "./api";
import { IDENTITY_KEY, readIdentity, saveIdentity } from "./identity";
import { usePolling } from "./hooks/usePolling";
import { ClassList } from "./components/ClassList";
import { IdentityPanel } from "./components/IdentityPanel";
import { Applications } from "./components/Applications";
import { AdminForm } from "./components/AdminForm";
import { Feedback, Loading } from "./components/Feedback";
import type { ClassOffering, StudentHold } from "./types";
import "./App.css";
const api = new AdmissionApi(
  import.meta.env.VITE_API_URL || "http://localhost:8080",
);
type View = "classes" | "applications" | "admin";
export default function App() {
  const [view, setView] = useState<View>("classes");
  const [studentId, setStudentId] = useState<string | null>(readIdentity);
  const session = useRef(studentId);
  const [busy, setBusy] = useState<string | null>(null);
  const action = useRef<AbortController | null>(null);
  const [notice, setNotice] = useState<{
    message: string;
    kind: "success" | "error" | "info";
  } | null>(null);
  const loadClasses = useCallback(
    (signal: AbortSignal) => api.classes(signal),
    [],
  );
  const loadHolds = useCallback(
    (signal: AbortSignal) =>
      studentId ? api.myHolds(studentId, signal) : Promise.resolve([]),
    [studentId],
  );
  const classes = usePolling(loadClasses, "classes");
  const applications = usePolling(loadHolds, studentId ?? "", !!studentId);
  const changeIdentity = useCallback((id: string | null, persist = true) => {
    action.current?.abort();
    action.current = null;
    setBusy(null);
    session.current = id;
    setStudentId(id);
    setNotice(null);
    if (persist && !saveIdentity(id))
      setNotice({
        kind: "info",
        message:
          "Browser storage is unavailable. Your identity will last only for this page session.",
      });
  }, []);
  useEffect(() => {
    const sync = (event: StorageEvent) => {
      if (event.key === IDENTITY_KEY || event.key === null)
        changeIdentity(readIdentity(), false);
    };
    window.addEventListener("storage", sync);
    return () => window.removeEventListener("storage", sync);
  }, [changeIdentity]);
  useEffect(
    () => () => {
      action.current?.abort();
    },
    [],
  );
  const refresh = useCallback(() => {
    classes.refresh();
    applications.refresh();
  }, [classes.refresh, applications.refresh]);
  const perform = async (
    key: string,
    work: (signal: AbortSignal) => Promise<unknown>,
    message: string,
    navigate = false,
  ): Promise<boolean> => {
    if (action.current) return false;
    const controller = new AbortController();
    action.current = controller;
    const owner = session.current;
    setBusy(key);
    setNotice(null);
    try {
      await work(controller.signal);
      if (controller.signal.aborted || session.current !== owner) return false;
      setNotice({ kind: "success", message });
      refresh();
      if (navigate) setView("applications");
      return true;
    } catch (error) {
      if (!controller.signal.aborted && session.current === owner) {
        setNotice({
          kind: "error",
          message:
            error instanceof Error
              ? error.message
              : "Unable to complete the request. Please retry.",
        });
        refresh();
      }
      return false;
    } finally {
      if (action.current === controller) {
        action.current = null;
        setBusy(null);
      }
    }
  };
  // A clock-expired card rechecks the backend once; normal polling keeps retrying network failures.
  const expirationRequests = useRef(new Map<string, AbortController>());
  useEffect(() => {
    const requests = expirationRequests.current;
    return () => {
      requests.forEach((controller) => controller.abort());
      requests.clear();
    };
  }, [studentId]);
  const onExpire = useCallback(
    (hold: StudentHold) => {
      if (expirationRequests.current.has(hold.holdId)) return;
      const controller = new AbortController();
      expirationRequests.current.set(hold.holdId, controller);
      void api
        .hold(hold.holdId, controller.signal)
        .catch(() => undefined)
        .finally(() => {
          if (!controller.signal.aborted) refresh();
        });
    },
    [refresh],
  );
  const offerings = classes.data ?? [];
  const holds = applications.data ?? [];
  const totals = offerings.reduce(
    (sum, c) => ({
      available: sum.available + c.availableSeats,
      confirmed: sum.confirmed + c.confirmedSeats,
      total: sum.total + c.totalSeats,
    }),
    { available: 0, confirmed: 0, total: 0 },
  );
  const apply = (offering: ClassOffering) => {
    if (studentId)
      void perform(
        `apply:${offering.id}`,
        (signal) => api.reserve(offering.id, studentId, signal),
        `A seat in ${offering.name} is reserved. Confirm admission before the reservation expires.`,
        true,
      );
  };
  const confirm = (hold: StudentHold) => {
    if (studentId)
      void perform(
        `confirm:${hold.holdId}`,
        (signal) => api.confirm(hold.holdId, studentId, signal),
        `Admission to ${hold.className} is confirmed. No payment was charged.`,
      );
  };
  const cancel = (hold: StudentHold) => {
    if (studentId)
      void perform(
        `cancel:${hold.holdId}`,
        (signal) => api.cancel(hold.holdId, studentId, signal),
        `Your reservation in ${hold.className} was cancelled.`,
      );
  };
  return (
    <div className="app-shell">
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <header className="site-header">
        <div className="header-inner">
          <a
            className="brand"
            href="#"
            onClick={(event) => {
              event.preventDefault();
              setView("classes");
            }}
          >
            <span className="brand-mark">
              <GraduationCap size={28} />
            </span>
            <span>
              School Admission
              <span className="brand-subtitle">
                A brighter future starts here
              </span>
            </span>
          </a>
          <span className="header-note">Admissions portal</span>
        </div>
      </header>
      <nav className="main-nav" aria-label="Main navigation">
        <div className="nav-inner">
          {(
            [
              { id: "classes", label: "Available classes", icon: BookOpen },
              {
                id: "applications",
                label: "My applications",
                icon: FileCheck2,
              },
              { id: "admin", label: "Admin", icon: Settings2 },
            ] as const
          ).map((item) => (
            <button
              key={item.id}
              className={view === item.id ? "nav-item active" : "nav-item"}
              aria-current={view === item.id ? "page" : undefined}
              onClick={() => {
                setView(item.id);
                setNotice(null);
              }}
            >
              <item.icon size={18} />
              {item.label}
            </button>
          ))}
        </div>
      </nav>
      <main id="main" className="main-container">
        <div className="page-heading">
          <div>
            <p className="section-label">
              {view === "admin" ? "Offering management" : "Student admissions"}
            </p>
            <h1>
              {view === "classes"
                ? "Find your next classroom."
                : view === "applications"
                  ? "Your applications, in one place."
                  : "Make room for the next generation."}
            </h1>
            <p className="muted">
              {view === "classes"
                ? "Explore class offerings, reserve a seat, and take the next step."
                : view === "applications"
                  ? "Track your reservations and confirm admission before time runs out."
                  : "Create class and shift offerings, and monitor seat availability."}
            </p>
          </div>
        </div>
        {notice && (
          <div className="notice-wrap">
            <Feedback {...notice} />
            <button
              className="text-button"
              aria-label="Dismiss notification"
              onClick={() => setNotice(null)}
            >
              Dismiss
            </button>
          </div>
        )}
        {view === "admin" && (
          <Feedback
            kind="info"
            message="Demo administration is open to everyone for this assignment. No administrator authentication is provided."
          />
        )}
        <div className={view === "admin" ? "admin-layout" : "content-layout"}>
          {view !== "admin" && (
            <IdentityPanel studentId={studentId} onChange={changeIdentity} />
          )}
          <div className="workspace">
            {view === "admin" && (
              <AdminForm
                busy={!!busy}
                onCreate={(name, seats) =>
                  perform(
                    "create-class",
                    (signal) => api.createClass(name, seats, signal),
                    `${name} was created with ${seats} seats.`,
                  )
                }
              />
            )}
            {view !== "applications" && (
              <div className="summary-strip">
                <div>
                  <span>Class offerings</span>
                  <strong>{classes.data ? offerings.length : "—"}</strong>
                </div>
                <div>
                  <span>Available seats</span>
                  <strong>{classes.data ? totals.available : "—"}</strong>
                </div>
                <div>
                  <span>Confirmed admissions</span>
                  <strong>{classes.data ? totals.confirmed : "—"}</strong>
                </div>
              </div>
            )}
            <div className="section-heading">
              <div>
                <h2>
                  {view === "applications"
                    ? "My applications"
                    : view === "admin"
                      ? "All class offerings"
                      : "Available class offerings"}
                </h2>
                <p className="small muted">
                  {view === "applications"
                    ? studentId
                      ? "Only applications for your current identity are shown."
                      : "Enter your student identity to view applications."
                    : "Seat counts refresh automatically every 4 seconds."}
                </p>
              </div>
              <button
                className="button secondary refresh"
                onClick={refresh}
                disabled={
                  classes.loading ||
                  (view === "applications" &&
                    (!studentId || applications.loading))
                }
              >
                <RefreshCw
                  size={15}
                  className={
                    classes.loading || applications.loading ? "spin" : ""
                  }
                />
                Refresh
              </button>
            </div>
            {view === "applications" ? (
              !studentId ? (
                <div className="empty panel">
                  <FingerprintPrompt />
                  <h3>Start with your student identity</h3>
                  <p>
                    Enter your birth registration number to restore your
                    applications from the admission service.
                  </p>
                </div>
              ) : (
                <>
                  {applications.error && (
                    <Feedback
                      message={applications.error}
                      retry={applications.refresh}
                    />
                  )}
                  {applications.loading && !applications.data ? (
                    <Loading label="Loading your applications…" />
                  ) : (
                    applications.data && (
                      <Applications
                        key={studentId}
                        holds={holds}
                        busy={busy}
                        onConfirm={confirm}
                        onCancel={cancel}
                        onExpire={onExpire}
                        onBrowse={() => setView("classes")}
                      />
                    )
                  )}
                </>
              )
            ) : (
              <>
                {classes.error && (
                  <Feedback message={classes.error} retry={classes.refresh} />
                )}
                {studentId && applications.error && view === "classes" && (
                  <Feedback
                    message={`Could not check your existing applications. ${applications.error}`}
                    retry={applications.refresh}
                  />
                )}
                {classes.loading && !classes.data ? (
                  <Loading label="Loading class offerings…" />
                ) : (
                  classes.data && (
                    <ClassList
                      classes={offerings}
                      holds={holds}
                      studentId={studentId}
                      busy={
                        busy ||
                        (studentId &&
                        (applications.loading || applications.error)
                          ? "checking-applications"
                          : null)
                      }
                      onApply={apply}
                      admin={view === "admin"}
                    />
                  )
                )}
              </>
            )}
            {view === "classes" && (
              <div className="how-it-works">
                <h2>A simple path to admission</h2>
                <ol>
                  <li>
                    <span>1</span>
                    <div>
                      <strong>Choose your class</strong>
                      <p>Find an offering with available seats.</p>
                    </div>
                  </li>
                  <li>
                    <span>2</span>
                    <div>
                      <strong>Reserve a seat</strong>
                      <p>Your reservation has a time limit.</p>
                    </div>
                  </li>
                  <li>
                    <span>3</span>
                    <div>
                      <strong>Confirm admission</strong>
                      <p>Complete simulated payment before expiry.</p>
                    </div>
                  </li>
                </ol>
                <button
                  className="text-button"
                  onClick={() => setView("applications")}
                >
                  View my applications <ArrowRight size={15} />
                </button>
              </div>
            )}
            {(view === "applications"
              ? applications.updated
              : classes.updated) && (
              <p className="sync-note">
                <CheckCircle2 size={13} />
                Last synced{" "}
                {new Date(
                  (view === "applications"
                    ? applications.updated
                    : classes.updated)!,
                ).toLocaleTimeString()}
              </p>
            )}
          </div>
        </div>
      </main>
      <footer className="site-footer">
        <GraduationCap size={18} />
        <span>School Admission System</span>
        <span>Student-first. One seat at a time.</span>
      </footer>
    </div>
  );
}
function FingerprintPrompt() {
  return <FileCheck2 size={30} />;
}
