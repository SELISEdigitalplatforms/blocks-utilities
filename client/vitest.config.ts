/// <reference types="vitest/config" />
import path from "path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      // Stub the design-system package in tests. Its barrel eagerly imports
      // framer-motion, whose motion-utils reads `process.env.NODE_ENV` at load
      // time and crashes under jsdom. The stub re-exports the repo's own
      // ui-kits primitives so component tests exercise real behavior. The
      // subpath entries must precede the bare-package entry: vite string
      // aliases match by prefix, so a bare "@seliseblocks/blocks-kit" listed
      // first would swallow "@seliseblocks/blocks-kit/hooks" and rewrite it to
      // a non-existent path.
      "@seliseblocks/blocks-kit/lib": path.resolve(
        __dirname,
        "./app/test-utils/stubs/blocks-kit.tsx",
      ),
      "@seliseblocks/blocks-kit/providers": path.resolve(
        __dirname,
        "./app/test-utils/stubs/blocks-kit.tsx",
      ),
      "@seliseblocks/blocks-kit/hooks": path.resolve(
        __dirname,
        "./app/test-utils/stubs/blocks-kit.tsx",
      ),
      "@seliseblocks/blocks-kit/layouts": path.resolve(
        __dirname,
        "./app/test-utils/stubs/blocks-kit.tsx",
      ),
      "@seliseblocks/blocks-kit": path.resolve(
        __dirname,
        "./app/test-utils/stubs/blocks-kit.tsx",
      ),
      "@": path.resolve(__dirname, "./app"),
      "@blocks-idp": path.resolve(__dirname, "./app/idp"),
      "@blocks-lmt": path.resolve(__dirname, "./app/cross-modules/lmt"),
      "@blocks-storage": path.resolve(__dirname, "./app/cross-modules/storage"),
      "@blocks-communication": path.resolve(
        __dirname,
        "./app/cross-modules/communication",
      ),
      "@blocks-identifier": path.resolve(
        __dirname,
        "./app/cross-modules/identifier",
      ),
      "@blocks-localization": path.resolve(
        __dirname,
        "./app/cross-modules/localization",
      ),
      "@blocks-utilities": path.resolve(
        __dirname,
        "./app/cross-modules/utilities",
      ),
      "@blocks-ai": path.resolve(__dirname, "./app/cross-modules/ai"),
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    /**
     * Raised from vitest's 5s default, which is too tight for what this suite actually does.
     *
     * `userEvent` simulates typing one keystroke at a time with a delay between them, so a test
     * that fills four fields — one of them a URL — spends seconds of wall clock doing nothing but
     * waiting. That fits in 5s on an idle machine and does not on a busy one: six unrelated files
     * failed on timeout under load while every one of them passed when run alone. A CI runner is a
     * busy machine by definition, and a gate that fails for that reason teaches people to re-run
     * it rather than read it.
     *
     * This does not hide a hung test, it only reports one later. The right narrower fix is
     * `userEvent.setup({ delay: null })` in the slow tests; this makes the suite trustworthy on a
     * shared runner today without editing several dozen files.
     */
    testTimeout: 20_000,
    hookTimeout: 20_000,
    server: {
      deps: {
        // genesis-os is shipped as an external dependency, so vite does not
        // rewrite import.meta.env inside it. Its runtime-env helper reads
        // import.meta.env[key] unguarded, which throws as soon as the barrel is
        // imported. Processing the package inline injects the env object.
        inline: ["@seliseblocks/genesis-os"],
      },
    },
    setupFiles: ["./app/test-utils/vitest.setup.ts"],
    coverage: {
      all: true,
      provider: "v8",
      reporter: ["text-summary", "json", "json-summary", "html"],
      include: ["app/**/*.{ts,tsx}"],
      exclude: [
        "app/**/*.test.*",
        "app/**/*.spec.*",
        "app/**/*.d.ts",
        "app/**/main.tsx",
        "app/**/vite-env.d.ts",
        "**/components/ui/**",
        "app/**/*.stories.*",
        "**/__generated__/**",
        "**/*.gen.*",
        "app/**/test-utils/**",
        "app/**/__mocks__/**",
      ],
    },
  },
});
