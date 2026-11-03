import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useAppSettingsStore, useI18nStore } from "@seliseblocks/genesis-os";
import { ThresholdChipInput } from "./threshold-chip-input";

describe("ThresholdChipInput language", () => {
  afterEach(() => {
    useAppSettingsStore.getState().setSettings({ language: "en" });
    useI18nStore.setState({ remote: {} });
  });

  it("shows English by default", () => {
    render(<ThresholdChipInput value={[]} onChange={vi.fn()} />);

    expect(screen.getByPlaceholderText("Custom %")).toBeInTheDocument();
  });

  it("shows German text when a German dictionary is registered", () => {
    useI18nStore.getState().setRemote("de", "utilities.subscription.plan-builder", {
      "THRESHOLD_CHIP_INPUT.CUSTOM_PLACEHOLDER": "Eigene %",
      "THRESHOLD_CHIP_INPUT.PRESS_ENTER": "Zum Hinzufügen Enter drücken",
    });
    useAppSettingsStore.getState().setSettings({ language: "de" });

    render(<ThresholdChipInput value={[]} onChange={vi.fn()} />);

    expect(screen.getByPlaceholderText("Eigene %")).toBeInTheDocument();
    expect(screen.getByText("Zum Hinzufügen Enter drücken")).toBeInTheDocument();
  });
});
