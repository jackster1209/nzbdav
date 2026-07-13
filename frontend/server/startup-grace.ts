/** Shared frontend-process clock for expected backend-not-ready noise. */
const startedAt = Date.now();
export const BACKEND_STARTUP_GRACE_MS = 30_000;
export const BACKEND_FAILURE_LOG_THROTTLE_MS = 60_000;

export function isWithinBackendStartupGrace(now = Date.now()): boolean {
  return now - startedAt < BACKEND_STARTUP_GRACE_MS;
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
