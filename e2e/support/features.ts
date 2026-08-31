/**
 * TypeScript mirror of features.mjs — keep both files in sync when adding features.
 * The runner reads features.mjs; this file is for IDE autocomplete in specs if needed.
 */
export type UtilitiesFeature = {
  id: string;
  name: string;
  enabled: boolean;
  spec: string;
};

export const UTILITIES_FEATURES: UtilitiesFeature[] = [
  {
    id: "overview",
    name: "Utilities – overview",
    enabled: true,
    spec: "tests/overview/overview.spec.ts",
  },
  {
    id: "create-payment",
    name: "Utilities – create payment",
    enabled: true,
    spec: "tests/payments/create-payment.spec.ts",
  },
  {
    id: "payment-list",
    name: "Utilities – payment list",
    enabled: true,
    spec: "tests/payments/payment-list.spec.ts",
  },
  {
    id: "saved-cards",
    name: "Utilities – saved cards",
    enabled: true,
    spec: "tests/payments/saved-cards.spec.ts",
  },
  {
    id: "payment-providers",
    name: "Utilities – payment providers",
    enabled: true,
    spec: "tests/payments/payment-providers.spec.ts",
  },
  {
    id: "magic-url",
    name: "Utilities – magic URL",
    enabled: true,
    spec: "tests/magic-url/magic-url.spec.ts",
  },
  {
    id: "billing-profile",
    name: "Utilities – magic URL",
    enabled: true,
    spec: "tests/subscriptions/billing.spec.ts",
  },
  {
    id: "discounts",
    name: "discounts",
    enabled: true,
    spec: "tests/subscriptions/discounts.spec.ts",
  },
  {
    id: "invoices",
    name: "invoices",
    enabled: true,
    spec: "tests/subscriptions/invoices.spec.ts",
  },
  {
    id: "merchant-profile",
    name: "merchant-profile",
    enabled: true,
    spec: "tests/subscriptions/merchant-profile.spec.ts",
  },
  {
    id: "plans",
    name: "plans",
    enabled: true,
    spec: "tests/subscriptions/plans.spec.ts",
  },
];
