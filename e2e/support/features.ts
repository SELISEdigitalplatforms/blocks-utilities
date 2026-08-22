/**
 * TypeScript mirror of features.mjs — keep both files in sync when adding features.
 * The runner reads features.mjs; this file is for IDE autocomplete in specs if needed.
 */
export type UtilitiesFeature = {
  id: string
  name: string
  enabled: boolean
  spec: string
}

export const UTILITIES_FEATURES: UtilitiesFeature[] = [
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
]
