export const AI_ENDPOINTS = {
  AGENT_QUERY_LMT_STREAM: "/ai-agent/query-lmt/stream",
  MODELS: "/model",
  MODEL_BY_ID: "/model/:id",
  MODEL_VALIDATE: "/model/:id/validate",
  MODEL_SEED_PROVIDERS: "/model/seed/providers",
  MODEL_SEED_BY_PROVIDER: "/model/seed/providers/:provider",
} as const;
