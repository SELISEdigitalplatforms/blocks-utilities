import { afterEach, describe, expect, it } from "vitest";
import { useAppSettingsStore, useI18nStore } from "@seliseblocks/genesis-os";
import { labelPlanChange } from "./plan-change-label";
import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";

const plan = { planId: "plan-a" } as SubscriptionPlan;

describe("plan-change-label.i18n language", () => {
  afterEach(() => {
    useAppSettingsStore.getState().setSettings({ language: "en" });
  });

  it("returns English by default", () => {
    expect(labelPlanChange(undefined, plan)).toBe("Switch plan");
  });

  it("returns German when a German dictionary is registered", () => {
    useI18nStore.getState().setRemote("de", "utilities.subscription-simulation", {
      "PLAN_CHANGE_LABEL.SWITCH_PLAN": "Plan wechseln",
    });
    useAppSettingsStore.getState().setSettings({ language: "de" });

    expect(labelPlanChange(undefined, plan)).toBe("Plan wechseln");
  });
});
