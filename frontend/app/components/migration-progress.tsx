import { useCallback, useEffect, useRef, useState } from "react";

export type MigrationStepStatus = "pending" | "running" | "completed" | "failed";

export type MigrationStep = {
  id: string;
  name: string;
  status: MigrationStepStatus;
  slow: boolean;
  startedAt: number | null;
  finishedAt: number | null;
};

export type MigrationStatus = {
  state: "running" | "completed" | "failed";
  startedAt: number;
  completed: number;
  total: number;
  currentStep: string | null;
  error: string | null;
  steps: MigrationStep[];
};

function isMigrationStatus(value: unknown): value is MigrationStatus {
  if (!value || typeof value !== "object") return false;
  const v = value as Record<string, unknown>;
  return typeof v.state === "string" && Array.isArray(v.steps);
}

function formatDuration(ms: number): string {
  if (!Number.isFinite(ms) || ms < 0) ms = 0;
  const totalSeconds = Math.floor(ms / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  const pad = (n: number) => String(n).padStart(2, "0");
  return hours > 0 ? `${hours}:${pad(minutes)}:${pad(seconds)}` : `${minutes}:${pad(seconds)}`;
}

type Phase = "checking" | "connecting" | "migrating" | "fallback";

type FallbackProps = {
  title: string;
  detail: string;
  showReload: boolean;
};

/**
 * Client-side wrapper rendered by the root ErrorBoundary. It polls
 * `/api/migration-status`; while the backend is applying database migrations
 * (the blocking startup phase) it renders a live progress page. Otherwise it
 * falls back to the generic error card the ErrorBoundary computed.
 */
export function MigrationBoundary({ fallback }: { fallback: FallbackProps }) {
  const [phase, setPhase] = useState<Phase>("checking");
  const [status, setStatus] = useState<MigrationStatus | null>(null);
  const seenMigration = useRef(false);
  const reloadScheduled = useRef(false);

  const scheduleReload = useCallback((delayMs: number) => {
    if (reloadScheduled.current) return;
    reloadScheduled.current = true;
    window.setTimeout(() => window.location.reload(), delayMs);
  }, []);

  useEffect(() => {
    let cancelled = false;

    const poll = async () => {
      try {
        const res = await fetch("/api/migration-status", {
          headers: { accept: "application/json" },
          cache: "no-store",
        });
        if (cancelled) return;

        if (res.ok) {
          const data = await res.json().catch(() => null);
          if (cancelled) return;
          if (isMigrationStatus(data)) {
            seenMigration.current = true;
            setStatus(data);
            setPhase("migrating");
            if (data.state === "completed") scheduleReload(1500);
            return;
          }
          // 200 but not migration-shaped: the real backend answered, so this
          // is a genuine error rather than the migration phase.
          setPhase("fallback");
          return;
        }

        // The backend port is not serving yet (starting up or handing off
        // from the migration process to the real backend). Reload periodically
        // so the real app loads as soon as the backend is reachable.
        if (res.status === 502 || res.status === 503) {
          setPhase("connecting");
          scheduleReload(5000);
          return;
        }

        setPhase("fallback");
      } catch {
        if (cancelled) return;
        // Network failure: nothing is listening on the backend port yet.
        setPhase("connecting");
        scheduleReload(5000);
      }
    };

    poll();
    const interval = window.setInterval(poll, 2000);
    return () => {
      cancelled = true;
      window.clearInterval(interval);
    };
  }, [scheduleReload]);

  if (phase === "migrating" && status) {
    return <MigrationProgressView status={status} />;
  }

  if (phase === "checking" || phase === "connecting") {
    return (
      <MigrationShell
        title={seenMigration.current ? "Finishing up" : "Connecting to nzbdav"}
        subtitle={
          seenMigration.current
            ? "Database maintenance finished. Waiting for the server to start..."
            : "Waiting for the backend to respond..."
        }
      >
        <div className="flex items-center gap-3 text-sm text-base-content/70">
          <span className="loading loading-spinner loading-sm text-primary" />
          <span>This can take a moment during startup.</span>
        </div>
      </MigrationShell>
    );
  }

  // Generic error fallback (mirrors the previous ErrorBoundary card).
  return (
    <MigrationShell title={fallback.title} subtitle={fallback.detail}>
      {fallback.showReload ? (
        <button
          type="button"
          className="btn btn-primary btn-sm"
          onClick={() => window.location.reload()}
        >
          Reload
        </button>
      ) : null}
    </MigrationShell>
  );
}

function MigrationProgressView({ status }: { status: MigrationStatus }) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const interval = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(interval);
  }, []);

  const total = status.total || status.steps.length;
  const completed = status.completed;
  const percent = total > 0 ? Math.min(100, Math.round((completed / total) * 100)) : 0;
  const overallElapsed = formatDuration(now - status.startedAt);
  const runningStep = status.steps.find((s) => s.status === "running") ?? null;
  const currentElapsed = runningStep?.startedAt ? formatDuration(now - runningStep.startedAt) : null;

  const failed = status.state === "failed";
  const done = status.state === "completed";

  let title = "Database maintenance in progress";
  let subtitle =
    "nzbdav is upgrading your database. This is a one-time step after an update and can take a while on large libraries. The app will load automatically when it finishes.";
  if (done) {
    title = "Maintenance complete";
    subtitle = "Starting nzbdav...";
  } else if (failed) {
    title = "Database maintenance failed";
    subtitle = "The upgrade could not be completed. Check the container logs for details.";
  }

  return (
    <MigrationShell title={title} subtitle={subtitle} wide>
      {failed && status.error ? (
        <div role="alert" className="alert alert-error text-xs">
          {status.error}
        </div>
      ) : null}

      <div className="space-y-2">
        <div className="flex items-center justify-between text-xs text-base-content/60">
          <span>
            Step {Math.min(completed + (done || failed ? 0 : 1), total)} of {total}
          </span>
          <span className="font-mono">Elapsed {overallElapsed}</span>
        </div>
        <progress
          className={`progress h-2 w-full ${failed ? "progress-error" : done ? "progress-success" : "progress-primary"}`}
          value={done ? 100 : percent}
          max={100}
        />
        {runningStep && !done && !failed ? (
          <div className="flex items-center gap-2 text-sm text-base-content/80">
            <span className="loading loading-spinner loading-sm text-primary" />
            <span>
              {runningStep.name}
              {currentElapsed ? <span className="ml-1 font-mono text-base-content/60">({currentElapsed})</span> : null}
            </span>
          </div>
        ) : null}
        {runningStep?.slow && !done && !failed ? (
          <div role="alert" className="alert alert-warning text-xs">
            This step rewrites large tables and may take a long time on big databases. This is expected.
          </div>
        ) : null}
      </div>

      <ul className="steps steps-vertical w-full">
        {status.steps.map((step) => (
          <li
            key={step.id}
            className={`step ${
              step.status === "completed"
                ? "step-success"
                : step.status === "running"
                  ? "step-primary"
                  : step.status === "failed"
                    ? "step-error"
                    : ""
            }`}
            aria-current={step.status === "running" ? "step" : undefined}
          >
            <span className="text-left text-sm">
              {step.name}
              {step.slow && step.status === "pending" ? (
                <span className="ml-2 badge badge-warning badge-xs">may be slow</span>
              ) : null}
            </span>
          </li>
        ))}
      </ul>

      {failed ? (
        <button
          type="button"
          className="btn btn-primary btn-sm"
          onClick={() => window.location.reload()}
        >
          Reload
        </button>
      ) : null}
    </MigrationShell>
  );
}

function MigrationShell({
  title,
  subtitle,
  children,
  wide,
}: {
  title: string;
  subtitle?: string;
  children?: React.ReactNode;
  wide?: boolean;
}) {
  return (
    <main className="hero min-h-dvh bg-base-300">
      <div className="hero-content w-full px-4 py-8">
        <div
          className={`card w-full ${wide ? "max-w-xl" : "max-w-lg"} border border-base-content/10 bg-base-100 shadow-xl`}
        >
          <div className="card-body gap-4">
            <div className="flex items-center gap-3">
              <img className="h-9 w-9" src="/logo.svg" alt="NzbDav" />
              <div className="space-y-1">
                <h1 className="text-xl font-bold tracking-tight">{title}</h1>
                {subtitle ? <p className="text-sm leading-relaxed text-base-content/70">{subtitle}</p> : null}
              </div>
            </div>
            {children}
          </div>
        </div>
      </div>
    </main>
  );
}
