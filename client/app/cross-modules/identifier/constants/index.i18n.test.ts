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

  it("returns the project language when its dictionary is registered", () => {
    useI18nStore.getState().setRemote("xx", "utilities.console", {
      "CONSTANTS.PRODUCTION": "[xx] Production",
    });
    useAppSettingsStore.getState().setSettings({ language: "xx" });

    expect(getRegisterServiceEnvironments()[0].label).toBe("[xx] Production");
  });
});
