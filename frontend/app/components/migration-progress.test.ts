import { describe, expect, it } from "vitest";
import { decideMigrationStatusPoll } from "./migration-progress";

describe("decideMigrationStatusPoll", () => {
  it("keeps polling while migration JSON is running", () => {
    const status = {
      state: "running" as const,
      startedAt: 1,
      completed: 0,
      total: 1,
      currentStep: "Metrics database",
      error: null,
      steps: [],
    };

    expect(decideMigrationStatusPoll(200, status)).toEqual({
      action: "migrating",
      status,
      reloadMs: undefined,
    });
  });

  it("reloads when migration completes", () => {
    const status = {
      state: "completed" as const,
      startedAt: 1,
      completed: 1,
      total: 1,
      currentStep: null,
      error: null,
      steps: [],
    };

    expect(decideMigrationStatusPoll(200, status)).toEqual({
      action: "migrating",
      status,
      reloadMs: 1500,
    });
  });

  it("treats 404 as backend handoff complete", () => {
    expect(decideMigrationStatusPoll(404, null)).toEqual({
      action: "connecting",
      reloadMs: 1500,
    });
  });

  it("treats 502/503 as connecting with a longer reload", () => {
    expect(decideMigrationStatusPoll(502, null)).toEqual({
      action: "connecting",
      reloadMs: 5000,
    });
    expect(decideMigrationStatusPoll(503, null)).toEqual({
      action: "connecting",
      reloadMs: 5000,
    });
  });

  it("falls back and stops for unexpected responses", () => {
    expect(decideMigrationStatusPoll(200, { hello: "world" })).toEqual({
      action: "fallback",
      stopPolling: true,
    });
    expect(decideMigrationStatusPoll(500, null)).toEqual({
      action: "fallback",
      stopPolling: true,
    });
  });
});
