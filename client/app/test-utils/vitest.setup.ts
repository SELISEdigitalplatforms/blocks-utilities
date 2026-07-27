/**
 * Global vitest setup for the jsdom environment.
 *
 * Adds jest-dom matchers and polyfills the browser globals that jsdom does not
 * provide (localStorage/sessionStorage on an opaque origin, matchMedia,
 * ResizeObserver, IntersectionObserver, scrollTo). Only patches when absent so
 * real browser-like environments and per-test overrides keep working.
 */
import "@testing-library/jest-dom/vitest";

// Some third-party ESM deps (e.g. framer-motion's motion-utils, pulled in via
// @seliseblocks/blocks-kit) read `process.env.NODE_ENV` at import time. Under the
// jsdom environment `process.env` can be undefined, which crashes module
// evaluation. Ensure a minimal, non-production process.env is always present.
{
  const g = globalThis as unknown as { process?: { env?: Record<string, string> } };
  if (!g.process) {
    g.process = { env: { NODE_ENV: "test" } };
  } else if (!g.process.env) {
    g.process.env = { NODE_ENV: "test" };
  } else if (g.process.env.NODE_ENV === undefined) {
    g.process.env.NODE_ENV = "test";
  }
}

class MemoryStorage implements Storage {
  private store = new Map<string, string>();

  get length(): number {
    return this.store.size;
  }

  clear(): void {
    this.store.clear();
  }

  getItem(key: string): string | null {
    return this.store.has(key) ? (this.store.get(key) as string) : null;
  }

  key(index: number): string | null {
    return Array.from(this.store.keys())[index] ?? null;
  }

  removeItem(key: string): void {
    this.store.delete(key);
  }

  setItem(key: string, value: string): void {
    this.store.set(key, String(value));
  }
}

function ensureStorage(name: "localStorage" | "sessionStorage"): void {
  let usable = false;
  try {
    const existing = (globalThis as Record<string, unknown>)[name] as
      | Storage
      | undefined;
    if (existing) {
      existing.setItem("__probe__", "1");
      existing.removeItem("__probe__");
      usable = true;
    }
  } catch {
    usable = false;
  }

  if (!usable) {
    const storage = new MemoryStorage();
    Object.defineProperty(globalThis, name, {
      value: storage,
      writable: true,
      configurable: true,
    });
    if (typeof window !== "undefined") {
      Object.defineProperty(window, name, {
        value: storage,
        writable: true,
        configurable: true,
      });
    }
  }
}

ensureStorage("localStorage");
ensureStorage("sessionStorage");

// Provide the runtime env the storage/logic services read via getRuntimeEnv
// (window.__BLOCKS_ENV__). The IAM base URL is intentionally left empty so
// IAM-scoped services produce relative URLs in tests. BLOCKS_X_BLOCKS_KEY must
// be present because @seliseblocks/blocks-kit instantiates its notification
// listener service at import time and reads that key through getRuntimeEnv;
// without it the kit's fallback to import.meta.env (undefined inside the
// pre-bundled dependency) throws and takes down every suite that imports it.
if (typeof window !== "undefined") {
  (window as unknown as { __BLOCKS_ENV__?: Record<string, string> }).__BLOCKS_ENV__ = {
    BLOCKS_LOGIC_BASE_URL: "https://dev-logic.blocksdevelopers.com",
    BLOCKS_X_BLOCKS_KEY: "test-blocks-key",
  };
}

if (typeof window !== "undefined" && typeof window.matchMedia !== "function") {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    configurable: true,
    value: (query: string): MediaQueryList =>
      ({
        matches: false,
        media: query,
        onchange: null,
        addListener: () => {},
        removeListener: () => {},
        addEventListener: () => {},
        removeEventListener: () => {},
        dispatchEvent: () => false,
      }) as unknown as MediaQueryList,
  });
}

if (typeof globalThis.ResizeObserver === "undefined") {
  class ResizeObserverStub {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  }
  (globalThis as Record<string, unknown>).ResizeObserver = ResizeObserverStub;
}

if (typeof globalThis.IntersectionObserver === "undefined") {
  class IntersectionObserverStub {
    readonly root = null;
    readonly rootMargin = "";
    readonly thresholds: ReadonlyArray<number> = [];
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
    takeRecords(): IntersectionObserverEntry[] {
      return [];
    }
  }
  (globalThis as Record<string, unknown>).IntersectionObserver =
    IntersectionObserverStub;
}

if (typeof window !== "undefined" && typeof window.scrollTo !== "function") {
  Object.defineProperty(window, "scrollTo", {
    writable: true,
    configurable: true,
    value: () => {},
  });
}

// jsdom does not implement these Element methods. Radix primitives (Select,
// Dialog, DropdownMenu, ...) and scroll containers call them during interaction,
// so provide inert stubs when absent to keep component tests from throwing.
if (typeof Element !== "undefined") {
  const proto = Element.prototype as unknown as Record<string, unknown>;
  if (typeof proto.scrollTo !== "function") {
    proto.scrollTo = () => {};
  }
  if (typeof proto.scrollIntoView !== "function") {
    proto.scrollIntoView = () => {};
  }
  if (typeof proto.hasPointerCapture !== "function") {
    proto.hasPointerCapture = () => false;
  }
  if (typeof proto.setPointerCapture !== "function") {
    proto.setPointerCapture = () => {};
  }
  if (typeof proto.releasePointerCapture !== "function") {
    proto.releasePointerCapture = () => {};
  }
}

// jsdom does not implement document.elementFromPoint. input-otp schedules a
// deferred call to it after a value change, which surfaces as an uncaught
// exception in OTP-based tests (mfa-check, profile-mfa-verify) even though the
// assertions pass. Provide an inert stub when absent.
if (
  typeof document !== "undefined" &&
  typeof document.elementFromPoint !== "function"
) {
  (document as unknown as { elementFromPoint: () => Element | null }).elementFromPoint =
    () => null;
}
