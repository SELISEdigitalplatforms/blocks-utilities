const ENV_PREFIX_PATTERN = /^(dev-|stg-)/;

function deriveBaseUrl(subdomain: string): string {
  if (typeof window === "undefined") {
    return `https://${subdomain}.blocksdevelopers.com`;
  }
  const origin = window.location.origin;
  const match = origin.match(/^https?:\/\/([^/]+)/);
  if (!match) {
    return `https://${subdomain}.blocksdevelopers.com`;
  }
  const host = match[1];
  if (!host.includes("localhost")) {
    const prefix = host.match(ENV_PREFIX_PATTERN)?.[1] ?? "";
    const derived = prefix ? `${prefix}${subdomain}` : subdomain;
    return `https://${derived}.blocksdevelopers.com`;
  }
  return `https://${subdomain}.blocksdevelopers.com`;
}

export function deriveIdpBaseUrl(): string {
  return deriveBaseUrl("idp");
}

export function deriveUdsBaseUrl(): string {
  return deriveBaseUrl("uds");
}
export function deriveAgentBaseUrl(): string {
  return deriveBaseUrl("agent");
}
export function deriveOsBaseUrl(): string {
  return deriveBaseUrl("os");
}
export function deriveEurolmBaseUrl(): string {
  return deriveBaseUrl("eurolm");
}
export function deriveLogicBaseUrl(): string {
  return deriveBaseUrl("logic");
}
export function deriveObservabilityBaseUrl(): string {
  return deriveBaseUrl("observability");
}
export function deriveDeploymentBaseUrl(): string {
  return deriveBaseUrl("deployment");
}
