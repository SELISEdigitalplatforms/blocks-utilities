import { afterEach, describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { useAppSettingsStore, useI18nStore } from "@seliseblocks/genesis-os";
import { PaymentStatusBadge } from "./payment-status-badge";

describe("PaymentStatusBadge language", () => {
  afterEach(() => {
    useAppSettingsStore.getState().setSettings({ language: "en" });
  });

  it("shows English by default", () => {
    render(<PaymentStatusBadge status="AUTHORIZED" />);

    expect(screen.getByText("Authorized")).toBeInTheDocument();
  });

  it("shows German text when a German dictionary is registered", () => {
    useI18nStore.getState().setRemote("de", "utilities.payment", {
      "PAYMENT_STATUS_BADGE.AUTHORIZED": "Autorisiert",
    });
    useAppSettingsStore.getState().setSettings({ language: "de" });

    render(<PaymentStatusBadge status="AUTHORIZED" />);

    expect(screen.getByText("Autorisiert")).toBeInTheDocument();
  });
});
