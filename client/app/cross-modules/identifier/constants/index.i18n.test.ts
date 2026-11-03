import { afterEach, describe, expect, it } from "vitest";
import { useAppSettingsStore, useI18nStore } from "@seliseblocks/genesis-os";
import { getRegisterServiceEnvironments } from "./index";

describe("index.i18n language", () => {
  afterEach(() => {
    useAppSettingsStore.getState().setSettings({ language: "en" });
  });

  it("returns English by default", () => {
    expect(getRegisterServiceEnvironments()[0].label).toBe("Production");
  });

  it("returns German when a German dictionary is registered", () => {
    useI18nStore.getState().setRemote("de", "utilities.console", {
      "CONSTANTS.PRODUCTION": "Produktion",
    });
    useAppSettingsStore.getState().setSettings({ language: "de" });

    expect(getRegisterServiceEnvironments()[0].label).toBe("Produktion");
  });
});
