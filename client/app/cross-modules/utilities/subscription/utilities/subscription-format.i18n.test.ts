import { afterEach, describe, expect, it } from "vitest";
import { useAppSettingsStore, useI18nStore } from "@seliseblocks/genesis-os";
import { formatInterval, formatMeterAllowance } from "./subscription-format";

describe("subscription formatters language", () => {
  afterEach(() => {
    useAppSettingsStore.getState().setSettings({ language: "en" });
    useI18nStore.setState({ remote: {} });
  });

  it("shows English by default", () => {
    expect(formatInterval("Month", 1)).toBe("every month");
  });

  it("shows German text when a German dictionary is registered", () => {
    useI18nStore.getState().setRemote("de", "utilities.subscription", {
      "SUBSCRIPTION_FORMAT.EVERY_MONTH_one": "jeden Monat",
      "SUBSCRIPTION_FORMAT.EVERY_MONTH_other": "alle {{count}} Monate",
      "SUBSCRIPTION_FORMAT.ALLOWANCE_INCLUDED_other": "{{quantity}} {{unit}} enthalten",
      "SUBSCRIPTION_FORMAT.ALLOWANCE_THEN_BLOCKED": "{{included}}, danach gesperrt",
    });
    useAppSettingsStore.getState().setSettings({ language: "de" });

    expect(formatInterval("Month", 1)).toBe("jeden Monat");
    expect(formatInterval("Month", 3)).toBe("alle 3 Monate");
    expect(
      formatMeterAllowance({
        displayName: "Calls",
        unitLabel: "Aufrufe",
        includedQuantity: 5,
        overageAllowed: false,
      }),
    ).toBe("5 Aufrufe enthalten, danach gesperrt");
  });
});
