import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, act } from "@testing-library/react";
import { BlocksLoginPage } from "./index";
import { BLOCKS_PRODUCTS } from "@/constants/blocks-products";

vi.mock("@/hooks/use-theme", () => ({
  useTheme: () => ({ setTheme: vi.fn(), resolvedTheme: "light" }),
}));

describe("BlocksLoginPage", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.runOnlyPendingTimers();
    vi.useRealTimers();
  });

  const product = BLOCKS_PRODUCTS[0];

  it("renders the active product hero and other services in the carousel", () => {
    render(<BlocksLoginPage name={product.name} onLogin={() => {}} />);
    expect(
      screen.getByText(`${BLOCKS_PRODUCTS.length - 1} services`),
    ).toBeInTheDocument();
    expect(screen.getByText(product.tagline)).toBeInTheDocument();
  });

  it("falls back to the first product for an unknown name", () => {
    render(<BlocksLoginPage name="does-not-exist" onLogin={() => {}} />);
    expect(screen.getByText(BLOCKS_PRODUCTS[0].tagline)).toBeInTheDocument();
  });

  it("invokes onLogin when the login button is clicked", () => {
    const onLogin = vi.fn();
    render(
      <BlocksLoginPage
        name={product.name}
        onLogin={onLogin}
        loginLabel="Sign in"
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Sign in" }));
    expect(onLogin).toHaveBeenCalledTimes(1);
  });

  it("disables the button and shows the redirect label while loading", () => {
    render(
      <BlocksLoginPage name={product.name} onLogin={() => {}} isLoading />,
    );
    const btn = screen.getByRole("button", { name: /Redirecting/ });
    expect(btn).toBeDisabled();
  });

  it("rotates the animated keyword on the interval", () => {
    render(<BlocksLoginPage name={product.name} onLogin={() => {}} />);
    expect(screen.getByText(product.keywords[0])).toBeInTheDocument();
    act(() => {
      vi.advanceTimersByTime(2800);
      vi.advanceTimersByTime(300);
    });
    expect(screen.getByText(product.keywords[1])).toBeInTheDocument();
  });

  it("renders a custom footer link", () => {
    render(
      <BlocksLoginPage
        name={product.name}
        onLogin={() => {}}
        footerLink={{ label: "Go Home", url: "https://example.com" }}
      />,
    );
    const link = screen.getByRole("link", { name: /Go Home/ });
    expect(link).toHaveAttribute("href", "https://example.com");
  });
});
