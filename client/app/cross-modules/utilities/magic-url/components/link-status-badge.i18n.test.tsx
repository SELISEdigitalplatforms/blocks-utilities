import { afterEach, describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { useAppSettingsStore, useI18nStore } from "@seliseblocks/genesis-os";
import { LinkStatusBadge } from "./link-status-badge";

describe("LinkStatusBadge language", () => {
  afterEach(() => {
    useAppSettingsStore.getState().setSettings({ language: "en" });
  });

  it("shows German text when a German dictionary is registered", () => {
    useI18nStore.getState().setRemote("de", "utilities.magic-url", {
      "LINK_STATUS_BADGE.ACTIVE": "Aktiv",
    });
    useAppSettingsStore.getState().setSettings({ language: "de" });

    render(<LinkStatusBadge status="Active" />);

    expect(screen.getByText("Aktiv")).toBeInTheDocument();
  });
});
