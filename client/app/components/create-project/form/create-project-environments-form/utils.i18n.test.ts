import { afterEach, describe, expect, it } from "vitest";
import { useAppSettingsStore, useI18nStore } from "@seliseblocks/genesis-os";
import { getEnvironmentOptions } from "./utils";

describe("utils.i18n language", () => {
  afterEach(() => {
    useAppSettingsStore.getState().setSettings({ language: "en" });
  });

  it("returns English by default", () => {
    expect(getEnvironmentOptions()[0].label).toBe("Development");
  });

  it("returns German when a German dictionary is registered", () => {
    useI18nStore.getState().setRemote("de", "utilities.shared", {
      "UTILS.DEVELOPMENT": "Entwicklung",
    });
    useAppSettingsStore.getState().setSettings({ language: "de" });

    expect(getEnvironmentOptions()[0].label).toBe("Entwicklung");
  });
});
