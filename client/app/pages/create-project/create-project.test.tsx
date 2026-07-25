import { describe, it, expect, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { NuqsTestingAdapter } from "nuqs/adapters/testing";
import { CreateProjectWrapper } from "./create-project";
import { useCreateProjectFormState } from "@/components/create-project/utils";

const renderPage = (search = "") =>
  render(
    <MemoryRouter>
      <NuqsTestingAdapter searchParams={search}>
        <CreateProjectWrapper />
      </NuqsTestingAdapter>
    </MemoryRouter>,
  );

describe("CreateProjectWrapper", () => {
  beforeEach(() => {
    useCreateProjectFormState.getState().resetFormData();
  });

  it("renders the first step naming form and heading", () => {
    renderPage();
    // The heading is rendered in both the mobile and desktop layouts.
    expect(screen.getAllByText("Create a project").length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText("Name your project").length).toBeGreaterThanOrEqual(1);
    expect(
      screen.getAllByPlaceholderText("Enter your project name").length,
    ).toBeGreaterThanOrEqual(1);
  });

  it("updates the project name field", () => {
    renderPage();
    const input = screen.getAllByPlaceholderText(
      "Enter your project name",
    )[0] as HTMLInputElement;
    fireEvent.change(input, { target: { value: "My Project" } });
    expect(input.value).toBe("My Project");
  });

  it("runs the tab-driven step effect without crashing", () => {
    // tab=2 exercises the effect that marks step 1 complete and resets the tab.
    renderPage("?tab=2");
    expect(screen.getAllByText("Create a project").length).toBeGreaterThanOrEqual(1);
  });
});
