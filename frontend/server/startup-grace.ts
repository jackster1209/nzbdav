/** Shared frontend-process clock for expected backend-not-ready noise. */
export const BACKEND_STARTUP_GRACE_MS = 30_000;
export const BACKEND_FAILURE_LOG_THROTTLE_MS = 60_000;

/** Uses process uptime so Express and SSR bundles agree even if this module is duplicated. */
export function isWithinBackendStartupGrace(uptimeMs = process.uptime() * 1000): boolean {
  return uptimeMs < BACKEND_STARTUP_GRACE_MS;
}

export function isExpectedBackendConnectionError(error: unknown): boolean {
  const candidates: unknown[] = [error];
  if (error && typeof error === "object") {
    const withCause = error as { cause?: unknown; errors?: unknown[] };
    if (withCause.cause) candidates.push(withCause.cause);
    if (Array.isArray(withCause.errors)) candidates.push(...withCause.errors);
  }

  return candidates.some((candidate) => {
    if (!candidate || typeof candidate !== "object") return false;
    const code = (candidate as { code?: string }).code;
    return code === "ECONNREFUSED" || code === "ECONNRESET" || code === "EPIPE";
  });
}

/** Match by name so callers need not import BackendUnavailableError across bundle boundaries. */
export function isExpectedBackendUnavailableError(error: unknown): boolean {
  if (error instanceof Error && error.name === "BackendUnavailableError") {
    return true;
  }
  return isExpectedBackendConnectionError(error);
}
