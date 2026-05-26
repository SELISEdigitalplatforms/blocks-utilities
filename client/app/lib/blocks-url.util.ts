const ENV_PREFIX_PATTERN = /^(dev-|stg-)/;
const LOCALHOST_PATTERN = /^(localhost|127\.0\.0\.1)$/;

function deriveBaseUrl(subdomain: string): string {
  const devStgDomain = `.blocksdevelopers.com`;
  const prodDomain = `.seliseblocks.com`;

  if (typeof window === "undefined") {
    return `https://stg-${subdomain}${devStgDomain}`;
  }

  const origin = window.location.origin;
  const match = origin.match(/^https?:\/\/([^/]+)/);
  if (!match) {
    return `https://stg-${subdomain}${devStgDomain}`;
  }

  const host = match[1];
  if (LOCALHOST_PATTERN.test(host)) {
    return `https://${subdomain}${devStgDomain}`;
  }

  // Check if the host is already a production domain (.seliseblocks.com)
  if (host.endsWith(prodDomain)) {
    const prefix = host.match(ENV_PREFIX_PATTERN)?.[1] ?? "";
    const derived = prefix ? `${prefix}${subdomain}` : subdomain;
    return `https://${derived}${prodDomain}`;
  }

  // For dev/stg environments on .blocksdevelopers.com
  const prefix = host.match(ENV_PREFIX_PATTERN)?.[1] ?? "";
  const derived = prefix ? `${prefix}${subdomain}` : subdomain;
  return `https://${derived}${devStgDomain}`;
}

export function deriveUtilityBaseUrl(): string {
  return deriveBaseUrl("utilities");
}

export function deriveIdpBaseUrl(): string {
  return deriveBaseUrl("iam");
}

export function deriveUdsBaseUrl(): string {
  return deriveBaseUrl("data");
}

export function deriveAgentBaseUrl(): string {
  return deriveBaseUrl("agent");
}
export function deriveOsBaseUrl(): string {
  return deriveBaseUrl("os");
}
export function deriveLocalizationBaseUrl(): string {
  return deriveBaseUrl("localization");
}
export function deriveLogicBaseUrl(): string {
  return deriveBaseUrl("logic");
}
export function deriveObservabilityBaseUrl(): string {
  return deriveBaseUrl("monitor");
}
export function deriveDeploymentBaseUrl(): string {
  return deriveBaseUrl("release");
}
