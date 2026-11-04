import { afterEach, describe, expect, it } from "vitest";
import { useAppSettingsStore, useI18nStore } from "@seliseblocks/genesis-os";
import { getUserStatusOptions } from "./user";

describe("user.i18n language", () => {
  afterEach(() => {
    useAppSettingsStore.getState().setSettings({ language: "en" });
  });

  it("returns English by default", () => {
    expect(getUserStatusOptions()[0].label).toBe("Active");
  });

  it("returns German when a German dictionary is registered", () => {
    useI18nStore.getState().setRemote("de", "utilities.idp", {
      "USER.ACTIVE": "Aktiv",
    });
    useAppSettingsStore.getState().setSettings({ language: "de" });

    expect(getUserStatusOptions()[0].label).toBe("Aktiv");
  });
});
