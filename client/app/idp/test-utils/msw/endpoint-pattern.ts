/**
 * An msw URL pattern for an endpoint URL: escaped, so a "." in the host matches only a dot, and anchored at the start
 * of the request URL (after an optional origin when the endpoint is a relative path). `suffix` is raw regex, e.g. "\\?"
 * or "$".
 */
export const endpointPattern = (url: string, suffix = ""): RegExp => {
  const escaped = url.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const origin = url.startsWith("/") ? "(?:https?://[^/]+)?" : "";
  return new RegExp(`^${origin}${escaped}${suffix}`);
};
