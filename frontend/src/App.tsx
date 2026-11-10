import { useState, useEffect } from 'react';
import {
  GraduationCap,
  CheckCircle2,
  XCircle,
  RefreshCw,
  Database,
  Server,
  Layers,
  Clock,
  ExternalLink,
} from 'lucide-react';
import './App.css';

interface HealthResponse {
  status: string;
  database: string;
  holdDurationSeconds?: number;
  timestamp: string;
  error?: string;
}

export default function App() {
  const [healthData, setHealthData] = useState<HealthResponse | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [latencyMs, setLatencyMs] = useState<number | null>(null);

  // Read backend API URL from environment variable (client-safe)
  const apiUrl = import.meta.env.VITE_API_URL || 'http://localhost:8080';

  const checkHealth = async () => {
    setLoading(true);
    setErrorMsg(null);
    const startTime = performance.now();
    try {
      const res = await fetch(`${apiUrl}/health`);
      const elapsed = Math.round(performance.now() - startTime);
      setLatencyMs(elapsed);

      const data: HealthResponse = await res.json();
      setHealthData(data);
      if (!res.ok) {
        setErrorMsg(data.error || 'Health check returned non-OK status');
      }
    } catch (err: unknown) {
      const elapsed = Math.round(performance.now() - startTime);
      setLatencyMs(elapsed);
      const message = err instanceof Error ? err.message : 'Unknown connection error';
      setErrorMsg(message);
      setHealthData(null);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    checkHealth();
  }, []);

  const isHealthy = healthData?.status === 'Healthy' && healthData?.database === 'Connected';

  return (
    <div className="app-container">
      <header className="header">
        <div className="logo-area">
          <GraduationCap className="logo-icon" />
          <span className="brand-title">School Admission System</span>
        </div>
        <span className="phase-tag">Phase 0: Scaffold & Infra</span>
      </header>

      <main className="main-content">
        <section className="hero-section">
          <h1 className="hero-title">School Admission Management</h1>
          <p className="hero-subtitle">
            Enterprise-grade admission orchestration system built with ASP.NET Core, PostgreSQL, and React.
          </p>
        </section>

        <div className="cards-grid">
          {/* Health Check Status Card */}
          <div className="card">
            <div className="card-header">
              <span className="card-title">
                <Server size={18} /> Backend & DB Health
              </span>
              <span
                className={`status-badge ${
                  loading ? 'checking' : isHealthy ? 'healthy' : 'unhealthy'
                }`}
              >
                {loading ? (
                  <RefreshCw size={12} className="spin" />
                ) : isHealthy ? (
                  <CheckCircle2 size={12} />
                ) : (
                  <XCircle size={12} />
                )}
                {loading ? 'Checking...' : isHealthy ? 'Healthy' : 'Unhealthy'}
              </span>
            </div>

            <div className="details-list">
              <div className="detail-item">
                <span className="detail-label">API Status</span>
                <span className="detail-value">{healthData?.status || (loading ? '...' : 'Offline')}</span>
              </div>
              <div className="detail-item">
                <span className="detail-label">Database Connection</span>
                <span className="detail-value">{healthData?.database || (loading ? '...' : 'Disconnected')}</span>
              </div>
              <div className="detail-item">
                <span className="detail-label">Hold Duration</span>
                <span className="detail-value">
                  {healthData?.holdDurationSeconds ?? 600}s (default)
                </span>
              </div>
              <div className="detail-item">
                <span className="detail-label">Response Time</span>
                <span className="detail-value">{latencyMs !== null ? `${latencyMs} ms` : 'N/A'}</span>
              </div>
              {errorMsg && (
                <div className="detail-item" style={{ color: 'var(--danger)' }}>
                  <span className="detail-label">Error</span>
                  <span className="detail-value">{errorMsg}</span>
                </div>
              )}
            </div>

            <div className="actions">
              <button
                className="btn btn-primary"
                onClick={checkHealth}
                disabled={loading}
              >
                <RefreshCw size={14} className={loading ? 'spin' : ''} />
                {loading ? 'Verifying...' : 'Re-check Health'}
              </button>
              <a
                href={`${apiUrl}/swagger`}
                target="_blank"
                rel="noreferrer"
                className="btn btn-secondary"
              >
                <ExternalLink size={14} /> Swagger UI
              </a>
            </div>
          </div>

          {/* Environment & Configuration Card */}
          <div className="card">
            <div className="card-header">
              <span className="card-title">
                <Layers size={18} /> Runtime Configuration
              </span>
              <span className="status-badge healthy">
                <CheckCircle2 size={14} /> Configured
              </span>
            </div>

            <div className="details-list">
              <div className="detail-item">
                <span className="detail-label">Frontend Origin</span>
                <span className="detail-value">http://localhost:5173</span>
              </div>
              <div className="detail-item">
                <span className="detail-label">Backend Target</span>
                <span className="detail-value">{apiUrl}</span>
              </div>
              <div className="detail-item">
                <span className="detail-label">Database Engine</span>
                <span className="detail-value">PostgreSQL 16 (Port 5432)</span>
              </div>
              <div className="detail-item">
                <span className="detail-label">
                  <Clock size={14} style={{ verticalAlign: 'middle', marginRight: 4 }} />
                  Seat Hold Window
                </span>
                <span className="detail-value">600 seconds (10 min)</span>
              </div>
            </div>

            <div className="actions">
              <a
                href={`${apiUrl}/health`}
                target="_blank"
                rel="noreferrer"
                className="btn btn-secondary"
              >
                <ExternalLink size={14} /> Direct Health Endpoint
              </a>
            </div>
          </div>
        </div>

        {/* Stack Overview */}
        <div className="card">
          <div className="card-header">
            <span className="card-title">
              <Database size={18} /> Phase 0 Foundation Stack
            </span>
            <span className="status-badge healthy">Ready for Phase 1</span>
          </div>

          <div className="stack-grid">
            <div className="stack-item">
              <div className="stack-item-title">Backend</div>
              <div className="stack-item-desc">ASP.NET Core Web API (.NET 8), Entity Framework Core</div>
            </div>
            <div className="stack-item">
              <div className="stack-item-title">Database</div>
              <div className="stack-item-desc">PostgreSQL 16 with Persistent Docker Volume</div>
            </div>
            <div className="stack-item">
              <div className="stack-item-title">Frontend</div>
              <div className="stack-item-desc">React 18+, Vite, TypeScript, Lucide Icons</div>
            </div>
            <div className="stack-item">
              <div className="stack-item-title">Orchestration</div>
              <div className="stack-item-desc">Docker Compose multi-service containerization</div>
            </div>
          </div>
        </div>
      </main>

      <footer className="footer">
        School Admission System &bull; Phase 0 Scaffold &bull; OnnoRokom Projukti Limited
      </footer>
    </div>
  );
}
