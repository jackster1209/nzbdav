import { describe, expect, it, vi } from "vitest";
import {
    canEditCategoryMappings,
    canEditReleaseSelection,
    isMigrationWorkActive,
    runUiMutation,
    type SessionStatus,
} from "./use-altmount-migration";

describe("isMigrationWorkActive", () => {
    it.each<SessionStatus>(["scanning", "running", "paused", "linking", "applying"])(
        "blocks destructive wizard actions while status is %s",
        (status) => expect(isMigrationWorkActive(status)).toBe(true),
    );

    it.each<SessionStatus>(["idle", "connected", "mapped", "scanned", "complete", "cancelled", "linked"])(
        "allows destructive wizard actions after status is %s",
        (status) => expect(isMigrationWorkActive(status)).toBe(false),
    );

    it("treats an unloaded status as inactive", () => {
        expect(isMigrationWorkActive(undefined)).toBe(false);
    });
});

describe("review mutation state guards", () => {
    it.each<SessionStatus>(["connected", "mapped", "scanned"])(
        "allows category mapping edits while status is %s",
        (status) => expect(canEditCategoryMappings(status)).toBe(true),
    );

    it.each<SessionStatus>(["idle", "scanning", "running", "paused", "complete", "cancelled", "linking", "linked", "applying"])(
        "locks category mapping edits while status is %s",
        (status) => expect(canEditCategoryMappings(status)).toBe(false),
    );

    it("allows release selection edits only for a completed scan", () => {
        const statuses: (SessionStatus | undefined)[] = [
            undefined, "idle", "connected", "mapped", "scanning", "running", "paused",
            "complete", "cancelled", "linking", "linked", "applying",
        ];
        expect(canEditReleaseSelection("scanned")).toBe(true);
        statuses.forEach((status) => expect(canEditReleaseSelection(status)).toBe(false));
    });
});

describe("runUiMutation", () => {
    it("returns true without recording an error after a successful mutation", async () => {
        const recordError = vi.fn();

        await expect(runUiMutation(() => Promise.resolve(), recordError)).resolves.toBe(true);
        expect(recordError).not.toHaveBeenCalled();
    });

    it("records a rejected mutation and returns false", async () => {
        const recordError = vi.fn();

        await expect(runUiMutation(
            () => Promise.reject(new Error("API rejected the mutation")),
            recordError,
        )).resolves.toBe(false);
        expect(recordError).toHaveBeenCalledOnce();
        expect(recordError).toHaveBeenCalledWith("API rejected the mutation");
    });
});
