import { describe, it, expect } from "vitest";
import { render, screen, renderHook } from "@testing-library/react";

import {
  BreadcrumbProvider,
  useBreadcrumbLabels,
  useDynamicBreadcrumbLabel,
} from "./breadcrumb-context";

const Labels = () => {
  const labels = useBreadcrumbLabels();
  return <div data-testid="labels">{JSON.stringify(labels)}</div>;
};

const Registrar = ({ href, label }: { href: string; label: string }) => {
  useDynamicBreadcrumbLabel(href, label);
  return null;
};

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

  it("registers a dynamic label and clears it when the consumer unmounts", () => {
    const { rerender } = render(
      <BreadcrumbProvider>
        <Registrar href="/a" label="Alpha" />
        <Labels />
      </BreadcrumbProvider>,
    );
    expect(screen.getByTestId("labels")).toHaveTextContent('{"/a":"Alpha"}');

    rerender(
      <BreadcrumbProvider>
        <Labels />
      </BreadcrumbProvider>,
    );
    expect(screen.getByTestId("labels")).toHaveTextContent("{}");
  });

  it("replaces the label when it changes", () => {
    const { rerender } = render(
      <BreadcrumbProvider>
        <Registrar href="/a" label="Alpha" />
        <Labels />
      </BreadcrumbProvider>,
    );
    rerender(
      <BreadcrumbProvider>
        <Registrar href="/a" label="Beta" />
        <Labels />
      </BreadcrumbProvider>,
    );
    expect(screen.getByTestId("labels")).toHaveTextContent('{"/a":"Beta"}');
  });

  it("registers nothing when the label is empty", () => {
    render(
      <BreadcrumbProvider>
        <Registrar href="/a" label="" />
        <Labels />
      </BreadcrumbProvider>,
    );
    expect(screen.getByTestId("labels")).toHaveTextContent("{}");
  });

  it("is a no-op without a provider", () => {
    expect(() =>
      render(<Registrar href="/a" label="Alpha" />),
    ).not.toThrow();
  });

  it("settles instead of re-registering on every provider render", () => {
    // The provider rebuilds its context value whenever a label changes, so the
    // effect must not depend on that value or it would loop forever.
    render(
      <BreadcrumbProvider>
        <Registrar href="/a" label="Alpha" />
        <Registrar href="/b" label="Beta" />
        <Labels />
      </BreadcrumbProvider>,
    );
    expect(screen.getByTestId("labels")).toHaveTextContent(
      '{"/a":"Alpha","/b":"Beta"}',
    );
  });
});
