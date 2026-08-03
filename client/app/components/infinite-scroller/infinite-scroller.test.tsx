import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { InfiniteScroll } from "./infinite-scroller";

type Item = { id: number };

const renderItem = (item: Item) => <div data-testid="row">row-{item.id}</div>;

const setScrollGeometry = (
  el: HTMLElement,
  geo: { scrollTop?: number; clientHeight?: number; scrollHeight?: number },
) => {
  Object.entries(geo).forEach(([k, v]) =>
    Object.defineProperty(el, k, {
      value: v,
      writable: true,
      configurable: true,
    }),
  );
};

const defaultProps = {
  renderItem,
  topFn: vi.fn(async () => [] as Item[]),
  pollingFn: vi.fn(async () => [] as Item[]),
  pollingInterval: 10_000,
  loadingIndicator: <div data-testid="loading">loading</div>,
  hasTopMore: true,
  bottomIndicator: (cb: () => void) => (
    <button data-testid="bottom" onClick={cb}>
      new
    </button>
  ),
};

describe("InfiniteScroll", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // jsdom does not implement Element.scrollTo, which the mount effect calls.
    Object.defineProperty(HTMLElement.prototype, "scrollTo", {
      value: vi.fn(),
      writable: true,
      configurable: true,
    });
  });
  afterEach(() => vi.restoreAllMocks());

  it("shows an empty state when there is no data", () => {
    render(<InfiniteScroll<Item> {...defaultProps} initialData={[]} />);
    expect(screen.getByText("No logs found")).toBeInTheDocument();
  });

  it("renders each item via renderItem", () => {
    render(
      <InfiniteScroll<Item>
        {...defaultProps}
        initialData={[{ id: 1 }, { id: 2 }]}
      />,
    );
    expect(screen.getAllByTestId("row")).toHaveLength(2);
  });

  it("fetches older data when scrolled to the top", async () => {
    const topFn = vi.fn(async () => [{ id: 0 }]);
    const { container } = render(
      <InfiniteScroll<Item>
        {...defaultProps}
        topFn={topFn}
        initialData={[{ id: 1 }]}
      />,
    );
    const scroller = container.querySelector(".overflow-scroll") as HTMLElement;
    setScrollGeometry(scroller, {
      scrollTop: 0,
      clientHeight: 100,
      scrollHeight: 200,
    });
    fireEvent.scroll(scroller);
    await waitFor(() => expect(topFn).toHaveBeenCalled());
    await waitFor(() => expect(screen.getAllByTestId("row")).toHaveLength(2));
  });

  it("stops fetching older data when the top function returns nothing", async () => {
    const topFn = vi.fn(async () => [] as Item[]);
    const { container } = render(
      <InfiniteScroll<Item>
        {...defaultProps}
        topFn={topFn}
        initialData={[{ id: 1 }]}
      />,
    );
    const scroller = container.querySelector(".overflow-scroll") as HTMLElement;
    setScrollGeometry(scroller, { scrollTop: 0, clientHeight: 100, scrollHeight: 200 });
    fireEvent.scroll(scroller);
    await waitFor(() => expect(topFn).toHaveBeenCalledTimes(1));
  });

  it("logs an error when fetching older data fails", async () => {
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    const topFn = vi.fn(async () => {
      throw new Error("boom");
    });
    const { container } = render(
      <InfiniteScroll<Item>
        {...defaultProps}
        topFn={topFn}
        initialData={[{ id: 1 }]}
      />,
    );
    const scroller = container.querySelector(".overflow-scroll") as HTMLElement;
    setScrollGeometry(scroller, { scrollTop: 0, clientHeight: 100, scrollHeight: 200 });
    fireEvent.scroll(scroller);
    await waitFor(() => expect(spy).toHaveBeenCalled());
  });

  it("polls for newer data and reveals the bottom indicator", async () => {
    vi.useFakeTimers();
    const pollingFn = vi.fn(async () => [{ id: 99 }]);
    render(
      <InfiniteScroll<Item>
        {...defaultProps}
        pollingFn={pollingFn}
        pollingInterval={1000}
        initialData={[{ id: 1 }]}
      />,
    );
    await vi.advanceTimersByTimeAsync(1000);
    vi.useRealTimers();
    await waitFor(() => expect(screen.getByTestId("bottom")).toBeInTheDocument());
  });

  it("hides the indicator when the bottom indicator is clicked", async () => {
    vi.useFakeTimers();
    const pollingFn = vi.fn(async () => [{ id: 99 }]);
    const { container } = render(
      <InfiniteScroll<Item>
        {...defaultProps}
        pollingFn={pollingFn}
        pollingInterval={1000}
        initialData={[{ id: 1 }]}
      />,
    );
    const scroller = container.querySelector(".overflow-scroll") as HTMLElement;
    scroller.scrollTo = vi.fn();
    await vi.advanceTimersByTimeAsync(1000);
    vi.useRealTimers();
    const btn = await screen.findByTestId("bottom");
    fireEvent.click(btn);
    await waitFor(() =>
      expect(screen.queryByTestId("bottom")).not.toBeInTheDocument(),
    );
  });
});
