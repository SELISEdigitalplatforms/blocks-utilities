import { describe, it, expect } from "vitest";
import { render, screen, renderHook } from "@testing-library/react";

import { BreadcrumbProvider, useBreadcrumbLabels } from "./breadcrumb-context";

describe("breadcrumb-context", () => {
  it("returns an empty label map when used without a provider", () => {
    const { result } = renderHook(() => useBreadcrumbLabels());
    expect(result.current).toEqual({});
  });

  it("renders the provider and exposes an initially empty label map to consumers", () => {
    const Consumer = () => {
      const labels = useBreadcrumbLabels();
      return <div data-testid="labels">{Object.keys(labels).length}</div>;
    };
    render(
      <BreadcrumbProvider>
        <Consumer />
      </BreadcrumbProvider>,
    );
    expect(screen.getByTestId("labels")).toHaveTextContent("0");
  });
});
