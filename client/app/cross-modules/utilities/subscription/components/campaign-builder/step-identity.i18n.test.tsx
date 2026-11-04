import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useAppSettingsStore, useI18nStore } from "@seliseblocks/genesis-os";
import { EMPTY_DRAFT } from "./campaign-draft";
import { StepIdentity } from "./step-identity";

describe("campaign StepIdentity language", () => {
  afterEach(() => {
    useAppSettingsStore.getState().setSettings({ language: "en" });
    useI18nStore.setState({ remote: {} });
  });

  it("shows English by default", () => {
    render(<StepIdentity draft={EMPTY_DRAFT} onChange={vi.fn()} />);

    expect(screen.getByRole("heading", { name: "Identity" })).toBeInTheDocument();
  });

  it("shows German text when a German dictionary is registered", () => {
    useI18nStore.getState().setRemote("de", "utilities.subscription.campaign-builder", {
      "STEP_IDENTITY.TITLE": "Allgemeines",
      "STEP_IDENTITY.KIND_STANDARD": "Standardrabatt",
    });
    useAppSettingsStore.getState().setSettings({ language: "de" });

    render(<StepIdentity draft={EMPTY_DRAFT} onChange={vi.fn()} />);

    expect(screen.getByRole("heading", { name: "Allgemeines" })).toBeInTheDocument();
    expect(screen.getByText("Standardrabatt")).toBeInTheDocument();
  });
});
