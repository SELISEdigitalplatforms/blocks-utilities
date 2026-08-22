import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { FormProvider, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  createSubscriptionPlanSchema,
  defaultSubscriptionPlanFormValues,
  type CreateSubscriptionPlanFormValues,
} from "../../schemas/subscription-plan.schema";
import { FLAT_FEE } from "../../schemas/subscription-price.schema";
import { QuantityDiscountTiers } from "./quantity-discount-tiers";

const item = (overrides: Partial<CreateSubscriptionPlanFormValues["quantityItems"][number]> = {}) => ({
  itemKey: "user",
  unitLabel: "user",
  minQuantity: 1,
  defaultQuantity: 1,
  quantityDiscountTiers: [],
  ...overrides,
});

const Harness = ({
  quantityItem = item(),
  priceQuantityItemKey = FLAT_FEE,
}: {
  quantityItem?: ReturnType<typeof item>;
  priceQuantityItemKey?: string;
}) => {
  const form = useForm<CreateSubscriptionPlanFormValues>({
    resolver: zodResolver(createSubscriptionPlanSchema),
    // The mode the real builder uses, so a message this test expects on blur is a message an
    // author actually sees on blur.
    mode: "onBlur",
    defaultValues: {
      ...defaultSubscriptionPlanFormValues,
      quantityItems: [quantityItem],
      prices: [
        {
          currencyCode: "CHF",
          amount: 145,
          interval: 2,
          intervalCount: 1,
          quantityItemKey: priceQuantityItemKey,
        },
      ],
    },
  });

  return (
    <FormProvider {...form}>
      <QuantityDiscountTiers itemIndex={0} />
    </FormProvider>
  );
};

/**
 * Read off the rendered inputs rather than the form object: it is what an author actually sees,
 * and capturing the form would mean assigning to an outer variable during render.
 */
const column = (field: string) =>
  Array.from(
    document.querySelectorAll<HTMLInputElement>(`input[name$=".${field}"]`),
  ).map((input) => input.value);

describe("QuantityDiscountTiers", () => {
  it("shows no bands until volume discounts are asked for", () => {
    render(<Harness />);

    expect(column("minimumQuantity")).toEqual([]);
  });

  it("opens with a usable pair rather than one row", () => {
    // One band is a flat discount, which the unit price already expresses — and the schema
    // refuses it, so seeding a single row would open the editor already invalid.
    render(<Harness />);

    fireEvent.click(screen.getByLabelText("Apply volume discounts"));

    expect(column("minimumQuantity")).toEqual(["1", "6"]);
  });

  it("starts the first band where the quantity item starts", () => {
    render(<Harness quantityItem={item({ minQuantity: 3, defaultQuantity: 3 })} />);

    fireEvent.click(screen.getByLabelText("Apply volume discounts"));

    expect(column("minimumQuantity")[0]).toBe("3");
  });

  /**
   * The regression this guards: the band being split is the open one, so it has no bound of its
   * own to build on. Derived from a constant, two bands landed on the same boundary the moment
   * this was clicked twice — which the server rejects as an overlap.
   */
  it("keeps added bands contiguous", () => {
    render(<Harness />);

    fireEvent.click(screen.getByLabelText("Apply volume discounts"));
    fireEvent.click(screen.getByText("Add band"));
    fireEvent.click(screen.getByText("Add band"));

    const from = column("minimumQuantity");
    const to = column("maximumQuantity");

    expect(from).toEqual(["1", "6", "11", "16"]);
    // Every band closes exactly one below the next, and only the last is left open.
    expect(to).toEqual(["5", "10", "15", ""]);
  });

  it("leaves the final band open on an item with no maximum", () => {
    render(<Harness />);

    fireEvent.click(screen.getByLabelText("Apply volume discounts"));

    expect(column("maximumQuantity").at(-1)).toBe("");
  });

  it("closes the final band at the item's maximum when it has one", () => {
    render(<Harness quantityItem={item({ maxQuantity: 30 })} />);

    fireEvent.click(screen.getByLabelText("Apply volume discounts"));

    expect(column("maximumQuantity").at(-1)).toBe("30");
  });

  it("refuses to remove a band while only two remain", () => {
    render(<Harness />);

    fireEvent.click(screen.getByLabelText("Apply volume discounts"));

    expect(screen.getByLabelText("Remove band 1")).toBeDisabled();
  });

  it("removes a band once there are more than two", () => {
    render(<Harness />);

    fireEvent.click(screen.getByLabelText("Apply volume discounts"));
    fireEvent.click(screen.getByText("Add band"));
    fireEvent.click(screen.getByLabelText("Remove band 1"));

    expect(column("minimumQuantity")).toHaveLength(2);
  });

  it("clears authored bands only after the author confirms", () => {
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);

    render(
      <Harness
        quantityItem={item({
          quantityDiscountTiers: [
            { minimumQuantity: 1, maximumQuantity: 4, discountPercent: 0 },
            { minimumQuantity: 5, discountPercent: 5 },
          ],
        })}
      />,
    );

    fireEvent.click(screen.getByLabelText("Apply volume discounts"));

    expect(confirm).toHaveBeenCalled();
    // The bands survive a declined confirm.
    expect(column("minimumQuantity")).toEqual(["1", "5"]);

    confirm.mockRestore();
  });

  it("does not ask before discarding bands nobody discounted", () => {
    // A confirm on every toggle trains people to dismiss it, which is how the one that mattered
    // gets dismissed too.
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);

    render(
      <Harness
        quantityItem={item({
          quantityDiscountTiers: [
            { minimumQuantity: 1, maximumQuantity: 4, discountPercent: 0 },
            { minimumQuantity: 5, discountPercent: 0 },
          ],
        })}
      />,
    );

    fireEvent.click(screen.getByLabelText("Apply volume discounts"));

    expect(confirm).not.toHaveBeenCalled();
    expect(column("minimumQuantity")).toEqual([]);

    confirm.mockRestore();
  });

  it("shows what a unit costs inside each band when one price applies", () => {
    render(<Harness quantityItem={item()} priceQuantityItemKey="user" />);

    fireEvent.click(screen.getByLabelText("Apply volume discounts"));

    // CHF 145 at 5% off. Presentation only: the server prices and rounds the charge.
    expect(screen.getByText(/137\.75/)).toBeInTheDocument();
  });

  it("shows no money when the item has no price of its own", () => {
    render(<Harness />);

    fireEvent.click(screen.getByLabelText("Apply volume discounts"));

    // With no price written against this item, "the" effective price is a fiction.
    expect(screen.queryByText(/CHF/)).not.toBeInTheDocument();
  });

  it("says the discount applies to the whole charge", () => {
    // Volume pricing, not graduated. An author who reads it the other way prices their catalogue
    // wrong and finds out from an invoice.
    render(<Harness />);

    fireEvent.click(screen.getByLabelText("Apply volume discounts"));

    expect(screen.getByText(/applies to the whole charge/)).toBeInTheDocument();
  });

  it("reports a gap on the band that starts in the wrong place", async () => {
    render(<Harness />);

    fireEvent.click(screen.getByLabelText("Apply volume discounts"));
    fireEvent.change(screen.getByLabelText("Band 2 from"), { target: { value: "9" } });
    fireEvent.blur(screen.getByLabelText("Band 2 from"));

    await waitFor(() => {
      expect(screen.getByText("The next band must begin at 6.")).toBeInTheDocument();
    });
  });
});
