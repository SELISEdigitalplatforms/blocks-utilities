/**
 * Centralized debug logger for OIDC and Impersonation flows.
 * Toggle via localStorage key: `DEBUG_OIDC_FLOW`
 * Set value to "true" to enable, anything else to disable.
 */
const NAMESPACE = "[OIDC-FLOW]";

function shouldLog(): boolean {
  try {
    return localStorage.getItem("DEBUG_OIDC_FLOW") === "true";
  } catch {
    return false;
  }
}

export const debug = {
  log: (...args: unknown[]) => {
    if (shouldLog()) console.log(NAMESPACE, ...args);
  },
  warn: (...args: unknown[]) => {
    if (shouldLog()) console.warn(NAMESPACE, ...args);
  },
  error: (...args: unknown[]) => {
    if (shouldLog()) console.error(NAMESPACE, ...args);
  },
  group: (label: string) => {
    if (shouldLog()) console.group(NAMESPACE, label);
  },
  groupEnd: () => {
    if (shouldLog()) console.groupEnd();
  },
  /** Dump current auth state from all relevant storages */
  dumpAuthState: () => {
    if (!shouldLog()) return;
    console.group(NAMESPACE, "Auth State Dump");
    try {
      const authStorage = localStorage.getItem("auth-storage");
      const oidcAuthStorage = localStorage.getItem("oidc-auth-storage");
      const oidcFlowParams = localStorage.getItem("oidc-flow-params");
      const impersonateStorage = localStorage.getItem("impersonate-storage");
      console.log("auth-storage:", authStorage ? JSON.parse(authStorage) : null);
      console.log("oidc-auth-storage:", oidcAuthStorage ? JSON.parse(oidcAuthStorage) : null);
      console.log("oidc-flow-params:", oidcFlowParams ? JSON.parse(oidcFlowParams) : null);
      console.log("impersonate-storage:", impersonateStorage ? JSON.parse(impersonateStorage) : null);
      console.log("window.location.href:", window.location.href);
    } catch (e) {
      console.error(NAMESPACE, "Failed to dump auth state:", e);
    }
    console.groupEnd();
  },
};
