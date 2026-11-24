import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { PlanMeter } from "../../models/subscription-plan.model";
import { ExistingMeterRatesFields } from "./existing-meter-rates-fields";

const meter = {
  meterKey: "api-calls",
  displayName: "API calls",
  unitLabel: "call",
  overageAllowed: true,
  rateTables: [{ currencyCode: "USD", tiers: [{ upToQuantity: null, unitAmountMinor: 200 }] }],
} as unknown as PlanMeter;

describe("ExistingMeterRatesFields", () => {
  it("leaves existing subscribers alone unless the box is ticked", async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    render(<ExistingMeterRatesFields meters={[meter]} onSave={onSave} />);

    fireEvent.click(screen.getByText("Save overage rates"));

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave.mock.calls[0][2]).toBe(false);
  });

  it("asks for existing subscribers to be repriced when the box is ticked", async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    render(<ExistingMeterRatesFields meters={[meter]} onSave={onSave} />);

    fireEvent.click(screen.getByLabelText(/Also apply to existing subscribers now/));
    fireEvent.click(screen.getByText("Save overage rates"));

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave).toHaveBeenCalledWith(
      "api-calls",
      [{ currencyCode: "USD", tiers: [{ upToQuantity: null, unitAmountMinor: 200 }] }],
      true,
    );
  });
});
