/**
 * Utilities E2E feature list — edit `enabled` and order here.
 * Run: npm run test:features
 *
 * Env: E2E_FEATURES=overview,create-payment  or  E2E_FEATURES=all
 */

/** @type {{ id: string, name: string, enabled: boolean, spec: string }[]} */
export const UTILITIES_FEATURES = [
  {
    id: "overview",
    name: "Utilities – overview",
    enabled: true,
    spec: "tests/01-overview/overview.spec.ts",
  },
  {
    id: "create-payment",
    name: "Utilities – create payment",
    enabled: true,
    spec: "tests/02-payments/create-payment.spec.ts",
  },
  {
    id: "payment-list",
    name: "Utilities – payment list",
    enabled: true,
    spec: "tests/02-payments/payment-list.spec.ts",
  },
  {
    id: "saved-cards",
    name: "Utilities – saved cards",
    enabled: true,
    spec: "tests/02-payments/saved-cards.spec.ts",
  },
  {
    id: "payment-providers",
    name: "Utilities – payment providers",
    enabled: true,
    spec: "tests/02-payments/payment-providers.spec.ts",
  },
  {
    id: "magic-url",
    name: "Utilities – magic URL",
    enabled: true,
    spec: "tests/04-magic-url/magic-url.spec.ts",
  },
  {
    id: "billing-profile",
    name: "Utilities – billing profile",
    enabled: true,
    spec: "tests/03-subscriptions/billing-profile.spec.ts",
  },
  {
    id: "discounts",
    name: "Utilities – discounts",
    enabled: true,
    spec: "tests/03-subscriptions/discounts.spec.ts",
  },
  {
    id: "invoices",
    name: "Utilities – invoices",
    enabled: true,
    spec: "tests/03-subscriptions/invoices.spec.ts",
  },
  {
    id: "merchant-profile",
    name: "Utilities – merchant profile",
    enabled: true,
    spec: "tests/03-subscriptions/merchant-profile.spec.ts",
  },
  {
    id: "plans",
    name: "Utilities – plans",
    enabled: true,
    spec: "tests/03-subscriptions/plans.spec.ts",
  },
];

export function resolveEnabledFeatures() {
  const override = process.env.E2E_FEATURES?.trim();

  if (!override || override === "all") {
    return UTILITIES_FEATURES.filter((feature) => feature.enabled);
  }

  const ids = override
    .split(",")
    .map((id) => id.trim())
    .filter(Boolean);
  /** @type {typeof UTILITIES_FEATURES} */
  const selected = [];

  for (const id of ids) {
    const feature = UTILITIES_FEATURES.find((entry) => entry.id === id);
    if (!feature) {
      throw new Error(
        `Unknown E2E feature "${id}". Valid ids: ${UTILITIES_FEATURES.map((f) => f.id).join(", ")}`,
      );
    }
    selected.push(feature);
  }

  return selected;
}
