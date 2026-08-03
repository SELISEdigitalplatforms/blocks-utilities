import { render, renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const setQueryParams = vi.fn();
let queryParams: Record<string, unknown>;

vi.mock("nuqs", () => ({
  useQueryStates: () => [queryParams, setQueryParams],
  parseAsString: { withDefault: (fallback: string) => fallback },
  parseAsInteger: { withDefault: (fallback: number) => fallback },
}));

type ToolbarProps = {
  values: Record<string, unknown>;
  onChange: (key: string, value: unknown) => void;
  onReset: () => void;
  filters: { key: string }[];
};

let lastProps: ToolbarProps;

vi.mock("@/components/filter-toolbar", () => ({
  FilterToolbar: (props: ToolbarProps) => {
    lastProps = props;
    return <div data-testid="filter-toolbar" />;
  },
  useSortQueryParams: (options: unknown) => ({ options }),
}));

import {
  MagicUrlsFilterToolBar,
  useMagicUrlSortQueryParams,
  useMagicUrlsFilterQueryParams,
} from "./magic-urls-filter-toolbar";

/** Runs the updater the component hands to nuqs and returns the new state. */
const applyUpdater = (previous: Record<string, unknown> = {}) => {
  const updater = setQueryParams.mock.calls.at(-1)?.[0];
  return typeof updater === "function" ? updater(previous) : updater;
};

describe("MagicUrlsFilterToolBar", () => {
  beforeEach(() => {
    setQueryParams.mockReset();
    queryParams = {
      search: "",
      expiryStartDate: "",
      expiryEndDate: "",
      status: "",
      requestMethod: "",
      type: "",
      page: 0,
      pageSize: 10,
    };
  });

  it("should offer a filter for each supported field", () => {
    render(<MagicUrlsFilterToolBar />);

    expect(lastProps.filters.map((filter) => filter.key)).toEqual([
      "search",
      "status",
      "requestMethod",
      "expiryDate",
    ]);
  });

  it("should pass the current query state through as the values", () => {
    queryParams = { ...queryParams, search: "invite", status: "Active" };

    render(<MagicUrlsFilterToolBar />);

    expect(lastProps.values.search).toBe("invite");
    expect(lastProps.values.status).toBe("Active");
  });

  it("should turn stored expiry dates back into dates", () => {
    queryParams = {
      ...queryParams,
      expiryStartDate: "2026-07-01T00:00:00.000Z",
      expiryEndDate: "2026-07-31T00:00:00.000Z",
    };

    render(<MagicUrlsFilterToolBar />);

    const expiry = lastProps.values.expiryDate as { from: Date; to: Date };
    expect(expiry.from).toBeInstanceOf(Date);
    expect(expiry.to).toBeInstanceOf(Date);
  });

  it("should leave the expiry range empty when nothing is stored", () => {
    render(<MagicUrlsFilterToolBar />);

    expect(lastProps.values.expiryDate).toEqual({ from: "", to: "" });
  });

  it("should record a simple filter and return to the first page", () => {
    render(<MagicUrlsFilterToolBar />);

    lastProps.onChange("status", "Active");

    expect(applyUpdater({ status: "", page: 3 })).toEqual(
      expect.objectContaining({ status: "Active", page: 0 }),
    );
  });

  it("should store an expiry range as ISO strings", () => {
    render(<MagicUrlsFilterToolBar />);
    const from = new Date("2026-07-01T00:00:00.000Z");
    const to = new Date("2026-07-31T00:00:00.000Z");

    lastProps.onChange("expiryDate", { from, to });

    expect(applyUpdater()).toEqual(
      expect.objectContaining({
        expiryStartDate: from.toISOString(),
        expiryEndDate: to.toISOString(),
        page: 0,
      }),
    );
  });

  it("should treat a single chosen day as both ends of the range", () => {
    render(<MagicUrlsFilterToolBar />);
    const only = new Date("2026-07-15T00:00:00.000Z");

    lastProps.onChange("expiryDate", { from: only });

    const next = applyUpdater();
    expect(next.expiryStartDate).toBe(only.toISOString());
    expect(next.expiryEndDate).toBe(only.toISOString());
  });

  it("should treat an end-only range the same way", () => {
    render(<MagicUrlsFilterToolBar />);
    const only = new Date("2026-07-15T00:00:00.000Z");

    lastProps.onChange("expiryDate", { to: only });

    const next = applyUpdater();
    expect(next.expiryStartDate).toBe(only.toISOString());
    expect(next.expiryEndDate).toBe(only.toISOString());
  });

  it("should clear the stored range when the picker is emptied", () => {
    render(<MagicUrlsFilterToolBar />);

    lastProps.onChange("expiryDate", null);

    expect(applyUpdater()).toEqual(
      expect.objectContaining({ expiryStartDate: "", expiryEndDate: "" }),
    );
  });

  it("should clear every filter on reset", () => {
    render(<MagicUrlsFilterToolBar />);

    lastProps.onReset();

    expect(setQueryParams).toHaveBeenCalledWith(null);
  });
});

describe("useMagicUrlsFilterQueryParams", () => {
  it("should expose the query state and its setter", () => {
    queryParams = { search: "abc" };

    const { result } = renderHook(() => useMagicUrlsFilterQueryParams());

    expect(result.current.queryParams).toBe(queryParams);
    expect(result.current.setQueryParams).toBe(setQueryParams);
  });
});

describe("useMagicUrlSortQueryParams", () => {
  it("should sort by operation name ascending by default", () => {
    const { result } = renderHook(() => useMagicUrlSortQueryParams());

    expect(result.current).toEqual({
      options: { initial: { property: "OperationName", isDescending: false } },
    });
  });
});
