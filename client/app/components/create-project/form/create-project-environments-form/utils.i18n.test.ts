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

  it("returns the project language when its dictionary is registered", () => {
    useI18nStore.getState().setRemote("xx", "utilities.shared", {
      "UTILS.DEVELOPMENT": "[xx] Development",
    });
    useAppSettingsStore.getState().setSettings({ language: "xx" });

    expect(getEnvironmentOptions()[0].label).toBe("[xx] Development");
  });
});
