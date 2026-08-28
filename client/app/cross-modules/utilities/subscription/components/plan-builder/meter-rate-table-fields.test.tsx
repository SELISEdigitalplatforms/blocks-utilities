import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { FormProvider, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  createSubscriptionPlanSchema,
  defaultSubscriptionPlanFormValues,
  type CreateSubscriptionPlanFormValues,
} from "../../schemas/subscription-plan.schema";
import { MeterRateTableFields } from "./meter-rate-table-fields";

const meter = {
  meterKey: "api-calls",
  displayName: "API calls",
  unitLabel: "call",
  aggregation: 0,
  includedQuantity: 150,
  overageAllowed: true,
  thresholdPercents: [],
  rateTables: [],
};

const Harness = () => {
  const form = useForm<CreateSubscriptionPlanFormValues>({
    resolver: zodResolver(createSubscriptionPlanSchema),
    defaultValues: { ...defaultSubscriptionPlanFormValues, meters: [meter] },
  });

  return (
    <FormProvider {...form}>
      <MeterRateTableFields meterIndex={0} />
    </FormProvider>
  );
};

/**
 * Read off the rendered inputs rather than the form object: it is what an author actually sees,
 * and capturing the form would mean assigning to an outer variable during render.
 */
const bounds = () =>
  Array.from(
    document.querySelectorAll<HTMLInputElement>('input[name$=".upToQuantity"]'),
  ).map((input) => input.value);

describe("MeterRateTableFields", () => {
  it("starts a table with a single unbounded band", () => {
    render(<Harness />);

    fireEvent.click(screen.getByText("Price the overage"));

    expect(bounds()).toEqual([""]);
  });

  /**
   * The regression this guards: the band being closed is the open-ended one, so it has no
   * bound of its own to reuse. Defaulting to a constant put two bands on the same bound the
   * moment this was clicked twice, which the server rejects outright.
   */
  it("gives each added band a bound above the one before it", () => {
    render(<Harness />);

    fireEvent.click(screen.getByText("Price the overage"));
    fireEvent.click(screen.getByText("Add another band"));
    fireEvent.click(screen.getByText("Add another band"));

    const [first, second, last] = bounds();

    expect(first).toBe("1000");
    expect(second).toBe("2000");
    expect(Number(second)).toBeGreaterThan(Number(first));
    expect(last).toBe("");
  });

  it("keeps the last band open however many are added", () => {
    render(<Harness />);

    fireEvent.click(screen.getByText("Price the overage"));
    fireEvent.click(screen.getByText("Add another band"));
    fireEvent.click(screen.getByText("Add another band"));

    const all = bounds();

    expect(all.at(-1)).toBe("");
    expect(all.slice(0, -1).every((bound) => bound !== "")).toBe(true);
  });
});

describe("the schema behind it", () => {
  const withTiers = (tiers: { upToQuantity?: number; unitAmount: number }[]) =>
    createSubscriptionPlanSchema.safeParse({
      ...defaultSubscriptionPlanFormValues,
      code: "metered",
      displayName: "Metered",
      organizationId: "__tenant_wide__",
      meters: [{ ...meter, rateTables: [{ currencyCode: "CHF", tiers }] }],
    });

  it("rejects two bands ending on the same quantity", () => {
    expect(
      withTiers([
        { upToQuantity: 1_000, unitAmount: 100 },
        { upToQuantity: 1_000, unitAmount: 50 },
        { unitAmount: 0 },
      ]).success,
    ).toBe(false);
  });

  it("rejects bands that descend", () => {
    expect(
      withTiers([
        { upToQuantity: 2_000, unitAmount: 100 },
        { upToQuantity: 1_000, unitAmount: 50 },
        { unitAmount: 0 },
      ]).success,
    ).toBe(false);
  });

  it("rejects an unbounded band that is not the last", () => {
    expect(
      withTiers([
        { unitAmount: 100 },
        { upToQuantity: 1_000, unitAmount: 50 },
      ]).success,
    ).toBe(false);
  });

  it("accepts ascending bands ending in an open one", () => {
    expect(
      withTiers([
        { upToQuantity: 1_000, unitAmount: 100 },
        { upToQuantity: 2_000, unitAmount: 50 },
        { unitAmount: 25 },
      ]).success,
    ).toBe(true);
  });
});

describe("the ordering error", () => {
  it("is shown against the table rather than failing silently", async () => {
    const Invalid = () => {
      const form = useForm<CreateSubscriptionPlanFormValues>({
        resolver: zodResolver(createSubscriptionPlanSchema),
        defaultValues: {
          ...defaultSubscriptionPlanFormValues,
          code: "metered",
          displayName: "Metered",
          meters: [
            {
              ...meter,
              rateTables: [
                {
                  currencyCode: "CHF",
                  tiers: [
                    { upToQuantity: 1_000, unitAmount: 100 },
                    { upToQuantity: 1_000, unitAmount: 50 },
                    { unitAmount: 0 },
                  ],
                },
              ],
            },
          ],
        },
      });

      return (
        <FormProvider {...form}>
          <button type="button" onClick={() => form.trigger()}>
            validate
          </button>
          <MeterRateTableFields meterIndex={0} />
        </FormProvider>
      );
    };

    render(<Invalid />);
    fireEvent.click(screen.getByText("validate"));

    await waitFor(() =>
      expect(screen.getByText(/must end above the one before it/)).toBeInTheDocument(),
    );
  });
});
