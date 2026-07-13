import { describe, expect, it } from "vitest";
import {
  isExpectedBackendConnectionError,
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
  });
});
