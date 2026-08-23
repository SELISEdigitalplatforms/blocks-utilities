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
      {/* The item's own bounds, registered the way the pricing step registers them. Typing into
          these is what puts strings in the form state — passing numeric defaults straight in
          never reproduces what an author actually does. */}
      <input aria-label="Item minimum" type="number" {...form.register("quantityItems.0.minQuantity")} />
      <input aria-label="Item maximum" type="number" {...form.register("quantityItems.0.maxQuantity")} />
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


  describe("narrow finite items", () => {
    it("splits the range instead of assuming five quantities fit", () => {
      // min 1 / max 5 used to open as 1-5 followed by 6-5: a second band starting above where it
      // ends, on an item whose ceiling the first band had already reached.
      render(<Harness quantityItem={item({ maxQuantity: 5 })} />);

      fireEvent.click(screen.getByLabelText("Apply volume discounts"));

      expect(column("minimumQuantity")).toEqual(["1", "5"]);
      expect(column("maximumQuantity")).toEqual(["4", "5"]);
    });

    it("keeps the first band inside an item narrower than the default step", () => {
      render(<Harness quantityItem={item({ maxQuantity: 3 })} />);

      fireEvent.click(screen.getByLabelText("Apply volume discounts"));

      expect(column("maximumQuantity")).toEqual(["2", "3"]);
    });

    it("opens valid on the narrowest item that can hold two bands", async () => {
      render(<Harness quantityItem={item({ maxQuantity: 2 })} />);

      fireEvent.click(screen.getByLabelText("Apply volume discounts"));

      expect(column("minimumQuantity")).toEqual(["1", "2"]);
      expect(column("maximumQuantity")).toEqual(["1", "2"]);

      // And the schema agrees, which is the point: the editor must not open into a state its own
      // validation refuses.
      fireEvent.blur(screen.getByLabelText("Band 2 from"));

      await waitFor(() => {
        expect(screen.queryByText(/must begin at/)).not.toBeInTheDocument();
      });
    });

    it("offers nothing to band on an item that allows one quantity", () => {
      render(<Harness quantityItem={item({ minQuantity: 5, maxQuantity: 5, defaultQuantity: 5 })} />);

      expect(screen.getByLabelText("Apply volume discounts")).toBeDisabled();
      expect(screen.getByText(/Raise its maximum/)).toBeInTheDocument();
    });

    it("stops splitting once the last band covers a single quantity", () => {
      render(<Harness quantityItem={item({ maxQuantity: 3 })} />);

      fireEvent.click(screen.getByLabelText("Apply volume discounts"));

      // Bands are 1-2 and 3-3. The last covers a single quantity, so splitting it again could
      // only produce a band starting above where it ends — the control is closed, not merely
      // guarded after the fact.
      expect(screen.getByText("Add band").closest("button")).toBeDisabled();
      expect(screen.getByText(/cannot be split again/)).toBeInTheDocument();

      fireEvent.click(screen.getByText("Add band"));

      expect(column("minimumQuantity")).toEqual(["1", "3"]);
    });
  });


  describe("bounds an author typed rather than defaults handed in", () => {
    it("adds to a typed minimum instead of concatenating onto it", () => {
      // A number input holds what was typed, so minQuantity arrives as "3". Read raw, "3" + 4 is
      // "34" and the second band began at 341 — a plan nobody could have meant.
      render(<Harness />);

      fireEvent.change(screen.getByLabelText("Item minimum"), { target: { value: "3" } });
      fireEvent.click(screen.getByLabelText("Apply volume discounts"));

      expect(column("minimumQuantity")).toEqual(["3", "8"]);
      expect(column("maximumQuantity")).toEqual(["7", ""]);
    });

    it("treats a maximum that was entered and then cleared as no maximum", () => {
      // Cleared, the input holds "" rather than undefined. Subtracted, that made an unbounded
      // item look like one with no room at all and disabled the control.
      render(<Harness />);

      const maximum = screen.getByLabelText("Item maximum");

      fireEvent.change(maximum, { target: { value: "20" } });
      fireEvent.change(maximum, { target: { value: "" } });

      expect(screen.getByLabelText("Apply volume discounts")).not.toBeDisabled();

      fireEvent.click(screen.getByLabelText("Apply volume discounts"));

      expect(column("maximumQuantity")).toEqual(["5", ""]);
    });

    it("respects a typed maximum when splitting the range", () => {
      render(<Harness />);

      fireEvent.change(screen.getByLabelText("Item maximum"), { target: { value: "4" } });
      fireEvent.click(screen.getByLabelText("Apply volume discounts"));

      expect(column("maximumQuantity")).toEqual(["3", "4"]);
    });

    it("keeps splitting a band whose bounds are two-digit strings", () => {
      // Compared as strings, "9" is greater than "10", so a band with room to spare reported
      // itself unsplittable — and one without room reported the opposite.
      render(<Harness />);

      fireEvent.change(screen.getByLabelText("Item maximum"), { target: { value: "10" } });
      fireEvent.click(screen.getByLabelText("Apply volume discounts"));

      expect(screen.getByText("Add band").closest("button")).not.toBeDisabled();

      fireEvent.click(screen.getByText("Add band"));

      expect(column("minimumQuantity")).toEqual(["1", "6", "10"]);
    });
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
