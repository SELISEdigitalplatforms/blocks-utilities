import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { StoredPaymentMethod } from "../models/stored-payment-method.model";

const toastMock = vi.fn();
const removeMethodMock = vi.fn();
const refetchMock = vi.fn();

let queryState: {
  data?: StoredPaymentMethod[];
  isLoading: boolean;
  isError: boolean;
  isFetching: boolean;
  error?: unknown;
};
let mutationState: { isPending: boolean; variables?: string };

vi.mock("@/hooks/use-toast", () => ({
  toast: (...args: unknown[]) => toastMock(...args),
}));

// The organization selector reaches IAM through react-query, which these tests render without
// a client. Stubbed rather than provided, because none of them are about the selector.
vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));

vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizations: () => ({
    data: {
      organizations: [{ itemId: "organization-2", name: "Test Org" }],
    },
  }),
}));

let requestedOrganizationId: string | undefined;

vi.mock("../hooks/use-stored-payment-methods", () => ({
  useStoredPaymentMethods: (organizationId?: string) => {
    requestedOrganizationId = organizationId;

    return { ...queryState, refetch: refetchMock };
  },
  useRemoveStoredPaymentMethod: () => ({
    mutateAsync: removeMethodMock,
    ...mutationState,
  }),
}));

import { StoredPaymentMethodsSection } from "./stored-payment-methods-section";

const method = (
  overrides: Partial<StoredPaymentMethod> = {},
): StoredPaymentMethod => ({
  paymentMethodId: "pm-1",
  brand: "visa",
  lastFour: "4242",
  type: "scheme",
  expiryMonth: "03",
  expiryYear: "2030",
  fundingSource: "credit",
  issuerCountry: "CH",
  status: "Active",
  ...overrides,
});

const manyMethods = (count: number) =>
  Array.from({ length: count }, (_, index) =>
    method({
      paymentMethodId: `pm-${index}`,
      lastFour: String(1000 + index),
      brand: index % 2 === 0 ? "visa" : "mc",
    }),
  );

describe("StoredPaymentMethodsSection", () => {
  beforeEach(() => {
    toastMock.mockReset();
    removeMethodMock.mockReset().mockResolvedValue("removed");
    refetchMock.mockReset();
    queryState = { data: [method()], isLoading: false, isError: false, isFetching: false };
    mutationState = { isPending: false, variables: undefined };
    requestedOrganizationId = undefined;
  });

  it("should ask for the caller's own organization until another is chosen", () => {
    render(<StoredPaymentMethodsSection />);

    expect(requestedOrganizationId).toBeUndefined();
  });

  /**
   * A failed load for one organization must still leave a way to switch to another, so the
   * selector lives outside the error branch.
   */
  it("should offer the organization selector even when the load failed", () => {
    queryState = {
      isLoading: false,
      isError: true,
      isFetching: false,
      error: new Error("nope"),
    };

    render(<StoredPaymentMethodsSection />);

    expect(screen.getByLabelText("Organization")).toBeTruthy();
  });

  it("should show a loading placeholder while the methods are being fetched", () => {
    queryState = { isLoading: true, isError: false, isFetching: true };

    render(<StoredPaymentMethodsSection />);

    expect(screen.getByLabelText("Loading saved payment methods")).toBeTruthy();
  });

  it("should report a failed load instead of an empty list", () => {
    queryState = { isLoading: false, isError: true, isFetching: false, error: new Error("nope") };

    render(<StoredPaymentMethodsSection />);

    expect(screen.getByText("Saved methods could not be loaded")).toBeTruthy();
  });

  it("should distinguish having no methods from having none that match", () => {
    queryState = { data: [], isLoading: false, isError: false, isFetching: false };

    render(<StoredPaymentMethodsSection />);

    expect(screen.getByText("No saved payment methods")).toBeTruthy();
  });

  it("should say the filters are the reason when a search matches nothing", () => {
    render(<StoredPaymentMethodsSection />);

    fireEvent.change(screen.getByLabelText("Search saved payment methods"), {
      target: { value: "nothing-matches-this" },
    });

    expect(screen.getByText("No matching payment methods")).toBeTruthy();
  });

  it("should match a search against the last four digits", () => {
    queryState = {
      data: [method(), method({ paymentMethodId: "pm-2", lastFour: "1111" })],
      isLoading: false,
      isError: false,
      isFetching: false,
    };

    render(<StoredPaymentMethodsSection />);
    fireEvent.change(screen.getByLabelText("Search saved payment methods"), {
      target: { value: "1111" },
    });

    expect(screen.queryAllByText("•••• •••• •••• 4242")).toHaveLength(0);
    expect(screen.getAllByText("•••• •••• •••• 1111").length).toBeGreaterThan(0);
  });

  it("should match a search case insensitively against the brand", () => {
    render(<StoredPaymentMethodsSection />);

    fireEvent.change(screen.getByLabelText("Search saved payment methods"), {
      target: { value: "VISA" },
    });

    expect(screen.getAllByText("•••• •••• •••• 4242").length).toBeGreaterThan(0);
  });

  it("should cap the search text so a paste cannot run away", () => {
    render(<StoredPaymentMethodsSection />);
    const search = screen.getByLabelText(
      "Search saved payment methods",
    ) as HTMLInputElement;

    fireEvent.change(search, { target: { value: "x".repeat(120) } });

    expect(search.value).toHaveLength(50);
  });

  it("should offer to clear the filters once one is applied", () => {
    render(<StoredPaymentMethodsSection />);
    fireEvent.change(screen.getByLabelText("Search saved payment methods"), {
      target: { value: "zzz" },
    });

    // The toolbar and the empty state each offer to clear; either does the job.
    fireEvent.click(screen.getAllByRole("button", { name: "Clear filters" })[0]);

    expect(
      (screen.getByLabelText("Search saved payment methods") as HTMLInputElement)
        .value,
    ).toBe("");
    expect(screen.getAllByText("•••• •••• •••• 4242").length).toBeGreaterThan(0);
  });

  it("should page the list and keep the pager in step", () => {
    queryState = {
      data: manyMethods(7),
      isLoading: false,
      isError: false,
      isFetching: false,
    };

    render(<StoredPaymentMethodsSection />);

    // Five per page by default, so the first page cannot go back.
    expect(
      screen.getByRole("button", { name: "Previous saved-method page" }),
    ).toBeDisabled();

    fireEvent.click(
      screen.getByRole("button", { name: "Next saved-method page" }),
    );

    expect(
      screen.getByRole("button", { name: "Next saved-method page" }),
    ).toBeDisabled();
    expect(
      screen.getByRole("button", { name: "Previous saved-method page" }),
    ).not.toBeDisabled();
  });

  it("should return to the first page when a filter narrows the list", () => {
    queryState = {
      data: manyMethods(7),
      isLoading: false,
      isError: false,
      isFetching: false,
    };

    render(<StoredPaymentMethodsSection />);
    fireEvent.click(
      screen.getByRole("button", { name: "Next saved-method page" }),
    );
    fireEvent.change(screen.getByLabelText("Search saved payment methods"), {
      target: { value: "visa" },
    });

    // Staying on page two would show an empty list under the new filter.
    expect(
      screen.getByRole("button", { name: "Previous saved-method page" }),
    ).toBeDisabled();
  });

  it("should ask for confirmation before removing a method", () => {
    render(<StoredPaymentMethodsSection />);

    fireEvent.click(
      screen.getByRole("button", { name: "Remove Visa ending in 4242" }),
    );

    expect(screen.getByText("Remove saved payment method?")).toBeTruthy();
    expect(removeMethodMock).not.toHaveBeenCalled();
  });

  it("should not remove anything when the confirmation is declined", () => {
    render(<StoredPaymentMethodsSection />);
    fireEvent.click(
      screen.getByRole("button", { name: "Remove Visa ending in 4242" }),
    );

    fireEvent.click(screen.getByRole("button", { name: "Keep method" }));

    expect(removeMethodMock).not.toHaveBeenCalled();
  });

  it("should remove the confirmed method and report success", async () => {
    render(<StoredPaymentMethodsSection />);
    fireEvent.click(
      screen.getByRole("button", { name: "Remove Visa ending in 4242" }),
    );

    fireEvent.click(screen.getByRole("button", { name: /Remove method|Remove$/ }));

    await waitFor(() => expect(removeMethodMock).toHaveBeenCalledWith("pm-1"));
    await waitFor(() =>
      expect(toastMock).toHaveBeenCalledWith(
        expect.objectContaining({ variant: "success" }),
      ),
    );
  });

  it("should report a provider-side removal as still processing", async () => {
    removeMethodMock.mockResolvedValue("pending");
    render(<StoredPaymentMethodsSection />);
    fireEvent.click(
      screen.getByRole("button", { name: "Remove Visa ending in 4242" }),
    );

    fireEvent.click(screen.getByRole("button", { name: /Remove method|Remove$/ }));

    await waitFor(() =>
      expect(toastMock).toHaveBeenCalledWith(
        expect.objectContaining({ variant: "info" }),
      ),
    );
  });

  it("should surface the reason a removal failed", async () => {
    removeMethodMock.mockRejectedValue(new Error("provider refused"));
    render(<StoredPaymentMethodsSection />);
    fireEvent.click(
      screen.getByRole("button", { name: "Remove Visa ending in 4242" }),
    );

    fireEvent.click(screen.getByRole("button", { name: /Remove method|Remove$/ }));

    await waitFor(() =>
      expect(toastMock).toHaveBeenCalledWith(
        expect.objectContaining({
          variant: "destructive",
          description: "provider refused",
        }),
      ),
    );
  });

  it("should fall back to a generic message when the failure carries none", async () => {
    removeMethodMock.mockRejectedValue("not an error object");
    render(<StoredPaymentMethodsSection />);
    fireEvent.click(
      screen.getByRole("button", { name: "Remove Visa ending in 4242" }),
    );

    fireEvent.click(screen.getByRole("button", { name: /Remove method|Remove$/ }));

    await waitFor(() =>
      expect(toastMock).toHaveBeenCalledWith(
        expect.objectContaining({
          variant: "destructive",
          description: "The saved payment method could not be removed.",
        }),
      ),
    );
  });

  it("should name the card being removed in the confirmation", () => {
    render(<StoredPaymentMethodsSection />);

    fireEvent.click(
      screen.getByRole("button", { name: "Remove Visa ending in 4242" }),
    );

    expect(screen.getByText(/ending in/)).toHaveTextContent("4242");
  });
});
