import { afterEach, describe, expect, it } from "vitest";
import { useAppSettingsStore, useI18nStore } from "@seliseblocks/genesis-os";
import { getUserCreationType } from "./authentication.constant";

describe("authentication.constant.i18n language", () => {
  afterEach(() => {
    useAppSettingsStore.getState().setSettings({ language: "en" });
  });

  it("returns English by default", () => {
    expect(getUserCreationType()[3]).toBe("Service");
  });

  it("returns German when a German dictionary is registered", () => {
    useI18nStore.getState().setRemote("de", "utilities.idp.authentication", {
      "AUTHENTICATION_CONSTANT.SERVICE": "Dienst",
    });
    useAppSettingsStore.getState().setSettings({ language: "de" });

    expect(getUserCreationType()[3]).toBe("Dienst");
  });
});
