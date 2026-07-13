import { describe, expect, it } from "vitest";
import { isHardFailureStatus } from "./error-donut";

describe("isHardFailureStatus", () => {
    it("treats Missing as a provider miss, not a hard failure", () => {
        expect(isHardFailureStatus("Missing")).toBe(false);
    });

    it("flags Timeout, Network, Auth, Corrupt, and Other as hard failures", () => {
        expect(isHardFailureStatus("Timeout")).toBe(true);
        expect(isHardFailureStatus("Network")).toBe(true);
        expect(isHardFailureStatus("Auth")).toBe(true);
        expect(isHardFailureStatus("Corrupt")).toBe(true);
        expect(isHardFailureStatus("Other")).toBe(true);
    });
});
