import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import {
  ToastProvider,
  ToastViewport,
  Toast,
  ToastTitle,
  ToastDescription,
  ToastClose,
  ToastAction,
} from "./toast";

const renderToast = (variant?: "default" | "destructive" | "success" | "warning" | "info") =>
  render(
    <ToastProvider>
      <Toast open variant={variant}>
        <ToastTitle>Saved</ToastTitle>
        <ToastDescription>Your changes were saved</ToastDescription>
        <ToastAction altText="undo">Undo</ToastAction>
        <ToastClose />
      </Toast>
      <ToastViewport />
    </ToastProvider>,
  );

describe("Toast", () => {
  it("renders the title, description and action", () => {
    renderToast();
    expect(screen.getByText("Saved")).toBeInTheDocument();
    expect(screen.getByText("Your changes were saved")).toBeInTheDocument();
    expect(screen.getByText("Undo")).toBeInTheDocument();
  });

  it("applies the destructive variant classes", () => {
    renderToast("destructive");
    expect(document.querySelector(".destructive")).toBeInTheDocument();
  });

  it("applies the success variant classes", () => {
    renderToast("success");
    expect(document.querySelector(".border-green-500")).toBeInTheDocument();
  });
});
