import { afterEach, describe, expect, it } from "vitest";
import { useAppSettingsStore, useI18nStore } from "@seliseblocks/genesis-os";
import { createSmsTemplateSchema } from "./sms.schema";

const firstMessage = () => {
  const result = createSmsTemplateSchema().safeParse({ name: "", language: "en", body: "x" });
  return result.success ? undefined : result.error.issues[0]?.message;
};

describe("sms schema language", () => {
  afterEach(() => {
    useAppSettingsStore.getState().setSettings({ language: "en" });
  });

  it("returns English messages by default", () => {
    expect(firstMessage()).toBe("Name is required.");
  });

  it("returns German messages when a German dictionary is registered", () => {
    useI18nStore.getState().setRemote("de", "utilities.sms", {
      "SMS_SCHEMA.NAME_IS_REQUIRED": "Name ist erforderlich.",
    });
    useAppSettingsStore.getState().setSettings({ language: "de" });

    expect(firstMessage()).toBe("Name ist erforderlich.");
  });
});
