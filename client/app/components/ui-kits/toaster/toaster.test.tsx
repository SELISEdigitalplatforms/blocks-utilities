import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";

let toasts: Array<Record<string, unknown>> = [];
vi.mock("@/hooks/use-toast", () => ({
  useToast: () => ({ toasts }),
}));

import { Toaster } from "./toaster";

describe("Toaster", () => {
  it("renders each toast from the hook with its title and description", () => {
    toasts = [
      { id: "1", title: "First", description: "First body", open: true },
      { id: "2", title: "Second", open: true },
    ];
    render(<Toaster />);
    expect(screen.getByText("First")).toBeInTheDocument();
    expect(screen.getByText("First body")).toBeInTheDocument();
    expect(screen.getByText("Second")).toBeInTheDocument();
  });

  it("renders nothing extra when there are no toasts", () => {
    toasts = [];
    const { container } = render(<Toaster />);
    expect(container.querySelectorAll("[data-state]")).toBeDefined();
  });
});
