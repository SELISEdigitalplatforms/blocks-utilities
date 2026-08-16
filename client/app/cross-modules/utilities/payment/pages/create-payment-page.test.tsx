import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import { CreatePaymentPage } from "./create-payment-page";

const { createPayment } = vi.hoisted(() => ({
  createPayment: vi.fn(),
}));

// The organization selector reaches IAM through react-query, which these tests render without a
// client. Stubbed rather than provided, because none of them are about the selector.
vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizations: () => ({
    data: { organizations: [{ itemId: "organization-2", name: "Test Org" }] },
    isError: false,
  }),
}));

vi.mock("../hooks/use-create-payment", () => ({
  useCreatePayment: () => ({
    mutateAsync: createPayment,
    isPending: false,
  }),
}));

describe("CreatePaymentPage", () => {
  it("allows Stripe to be selected as the payment provider", async () => {
    const user = userEvent.setup();

    createPayment.mockResolvedValue({
      paymentDetailId: "payment-1",
      providerName: "STRIPE",
      paymentStatus: "PROCESSING",
      orderId: "TEST-ORDER-001",
      amount: 10,
      currencyCode: "CHF",
      redirectUrl: "https://checkout.stripe.example/session",
      expiresAtUtc: null,
    });

    vi.spyOn(window, "open").mockReturnValue(null);

    render(
      <MemoryRouter>
        <CreatePaymentPage />
      </MemoryRouter>,
    );

    const provider = screen.getAllByRole("combobox")[0];

    expect(provider).toHaveTextContent("Adyen");

    await user.click(provider);
    await user.click(
      await screen.findByRole("option", { name: "Stripe" }),
    );

    expect(provider).toHaveTextContent("Stripe");

    await user.click(
      screen.getByRole("button", {
        name: "Create and open checkout",
      }),
    );

    await waitFor(() =>
      expect(createPayment).toHaveBeenCalledWith(
        expect.objectContaining({
          request: expect.objectContaining({
            providerName: "STRIPE",
          }),
        }),
      ),
    );
  });
});
