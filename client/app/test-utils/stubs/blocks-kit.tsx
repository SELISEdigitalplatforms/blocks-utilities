/**
 * Test-only stub for `@seliseblocks/blocks-kit`.
 *
 * The real package's barrel eagerly imports framer-motion, whose `motion-utils`
 * reads `process.env.NODE_ENV` at import time and throws under the jsdom test
 * environment. Aliasing the package to this stub keeps the heavy design-system
 * (and its animation deps) out of the test module graph while still providing
 * the handful of exports the app modules under test rely on.
 *
 * `useProjectStore` / `useAuthStore` are re-exported from the local stores so
 * that `vi.mock("@/store/useProjectStore", ...)` in hook tests continues to
 * intercept them.
 */
import React from "react";

export { useProjectStore } from "@/store/useProjectStore";
export { useAuthStore } from "@/store/useAuthStore";

type HttpClientOptions = { baseURL?: string; blocksKey?: string };

/**
 * Minimal HttpClient stand-in. Service tests mock `@/lib/http-client`, so these
 * methods are rarely invoked; they exist so constructing the client at module
 * load time does not throw.
 */
export class HttpClient {
  baseURL: string;
  blocksKey: string;

  constructor(options: HttpClientOptions = {}) {
    this.baseURL = options.baseURL ?? "";
    this.blocksKey = options.blocksKey ?? "";
  }

  get<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
  post<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
  put<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
  patch<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
  delete<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
  stream<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
}

export class HttpError extends Error {
  status: number;
  errors: Record<string, string | string[]>;
  constructor(status = 0, errors: Record<string, string | string[]> = {}) {
    super(`HttpError ${status}`);
    this.status = status;
    this.errors = errors;
  }
}

type PassthroughProps = { children?: React.ReactNode } & Record<string, unknown>;

const passthrough = (label: string) => {
  const Component = ({ children }: PassthroughProps) =>
    React.createElement(React.Fragment, null, children);
  Component.displayName = label;
  return Component;
};

// Layout / provider components — rendered as transparent passthroughs.
export const BlocksAppLayout = passthrough("BlocksAppLayout");
export const TooltipProvider = passthrough("TooltipProvider");
export const ConsoleLayout = passthrough("ConsoleLayout");
export const ConsolePage = passthrough("ConsolePage");
export const DashboardOverview = passthrough("DashboardOverview");
export const DashboardRoute = passthrough("DashboardRoute");
export const LoginPage = passthrough("LoginPage");
export const CallbackPage = passthrough("CallbackPage");
export const AuthResolver = passthrough("AuthResolver");
export const ProtectedGuard = passthrough("ProtectedGuard");
export const PublicGuard = passthrough("PublicGuard");
