const ENV_PREFIX_PATTERN = /^(dev-|stg-)/;
const LOCALHOST_PATTERN = /^(localhost|127\.0\.0\.1)$/;

function deriveBaseUrl(subdomain: string): string {
  const baseDomain = `.blocksdevelopers.com`;
  
  if (typeof window === "undefined") {
    return `https://dev-${subdomain}${baseDomain}`;
  }
  
  const origin = window.location.origin;
  const match = origin.match(/^https?:\/\/([^/]+)/);
  if (!match) {
    return `https://dev-${subdomain}${baseDomain}`;
  }
  
  const host = match[1];
  if (!LOCALHOST_PATTERN.test(host)) {
    const prefix = host.match(ENV_PREFIX_PATTERN)?.[1] ?? "";
    const derived = prefix ? `${prefix}${subdomain}` : subdomain;
    return `https://${derived}${baseDomain}`;
  }
  return `https://${subdomain}${baseDomain}`;
}

export function deriveUtilityBaseUrl(): string {
  return deriveBaseUrl("utility");
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
