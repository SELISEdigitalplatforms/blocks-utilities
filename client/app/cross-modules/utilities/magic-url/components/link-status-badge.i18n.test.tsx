import { afterEach, describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { useAppSettingsStore, useI18nStore } from "@seliseblocks/genesis-os";
import { LinkStatusBadge } from "./link-status-badge";

describe("LinkStatusBadge language", () => {
  afterEach(() => {
    useAppSettingsStore.getState().setSettings({ language: "en" });
  });

  it("shows the project language when its dictionary is registered", () => {
    useI18nStore.getState().setRemote("xx", "utilities.magic-url", {
      "LINK_STATUS_BADGE.ACTIVE": "[xx] Active",
    });
    useAppSettingsStore.getState().setSettings({ language: "xx" });

    render(<LinkStatusBadge status="Active" />);

    expect(screen.getByText("[xx] Active")).toBeInTheDocument();
  });
});
