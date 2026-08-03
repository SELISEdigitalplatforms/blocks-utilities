import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { describe, expect, it } from "vitest";
import { PaymentResultPage } from "./payment-result-page";

const renderResultPage = (query: string) =>
  render(
    <MemoryRouter
      initialEntries={[`/app/project-1/payment/result${query}`]}
    >
      <Routes>
        <Route
          path="/app/:itemId/payment/result"
          element={<PaymentResultPage />}
        />
      </Routes>
    </MemoryRouter>,
  );

describe("PaymentResultPage", () => {
  it("shows the success state and safe payment identifier", () => {
    renderResultPage(
      "?status=success&paymentDetailId=payment-123",
    );

    expect(
      screen.getByRole("heading", { name: "Payment completed" }),
    ).toBeInTheDocument();
    expect(screen.getByText("payment-123")).toBeInTheDocument();
  });

  it.each(["cancelled", "canceled"])(
    "shows the cancelled state for %s",
    (status) => {
      renderResultPage(`?status=${status}`);

      expect(
        screen.getByRole("heading", {
          name: "Payment cancelled",
        }),
      ).toBeInTheDocument();
    },
  );

  it("does not treat an unknown query value as a confirmed result", () => {
    renderResultPage("?status=authorised");

    expect(
      screen.getByRole("heading", {
        name: "Payment result unavailable",
      }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("alert"),
    ).toHaveTextContent(
      "Only success, fail, cancelled, or pending are recognized.",
    );
  });
});
