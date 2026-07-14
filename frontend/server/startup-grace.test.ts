import { describe, expect, it } from "vitest";
import {
  BACKEND_STARTUP_GRACE_MS,
  isExpectedBackendConnectionError,
  isExpectedBackendUnavailableError,
  isWithinBackendStartupGrace,
} from "./startup-grace";

describe("startup-grace helpers", () => {
  it("recognizes ECONNREFUSED AggregateErrors", () => {
    const error = Object.assign(new Error("proxy failed"), {
      code: undefined,
      cause: Object.assign(new Error("refused"), {
        code: "ECONNREFUSED",
        errors: [{ code: "ECONNREFUSED" }, { code: "ECONNREFUSED" }],
      }),
    });

    expect(isExpectedBackendConnectionError(error)).toBe(true);
    expect(isExpectedBackendConnectionError(new Error("boom"))).toBe(false);
  });

  it("reports within the startup grace window for a fresh process", () => {
    expect(isWithinBackendStartupGrace()).toBe(true);
    expect(isWithinBackendStartupGrace(0)).toBe(true);
    expect(isWithinBackendStartupGrace(BACKEND_STARTUP_GRACE_MS)).toBe(false);
  });

  it("recognizes BackendUnavailableError by name", () => {
    const error = new Error("Failed to fetch onboarding status: fetch failed");
    error.name = "BackendUnavailableError";

    expect(isExpectedBackendUnavailableError(error)).toBe(true);
    expect(isExpectedBackendUnavailableError(new Error("boom"))).toBe(false);
  });

  it("treats connection errors as expected backend unavailability", () => {
    const error = Object.assign(new Error("connect failed"), { code: "ECONNREFUSED" });
    expect(isExpectedBackendUnavailableError(error)).toBe(true);
  });
});
