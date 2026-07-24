module.exports = {
  root: true,
  env: {
    browser: true,
    es2021: true,
  },
  parser: "@typescript-eslint/parser",
  parserOptions: {
    ecmaVersion: "latest",
    sourceType: "module",
    ecmaFeatures: {
      jsx: true,
    },
  },
  settings: {
    react: {
      version: "detect",
    },
  },
  plugins: ["@typescript-eslint", "react", "react-hooks"],
  extends: [
    "eslint:recommended",
    "plugin:@typescript-eslint/recommended",
    "plugin:react/recommended",
    "plugin:react-hooks/recommended",
  ],
  rules: {
    "react/react-in-jsx-scope": "off",
    "react/jsx-uses-react": "off",
    "no-unused-vars": "off",
    "no-console": ["error", { allow: ["error"] }],
    "@typescript-eslint/no-empty-object-type": [
      "error",
      { allowInterfaces: "with-single-extends" },
    ],
    "@typescript-eslint/no-unused-vars": [
      "warn",
      {
        argsIgnorePattern: "^_",
        varsIgnorePattern: "^_",
        caughtErrorsIgnorePattern: "^_",
      },
    ],
    // Naming conventions (see CONTRIBUTING.md). Authored to reflect the style already in
    // use so it lands green; severity is "warn" and will be ratcheted toward "error" later.
    // Filename conventions (kebab-case, use-* hooks, *.service.ts) are documented in
    // CONTRIBUTING.md; enforcing them in-lint needs eslint-plugin-unicorn, deferred here to
    // avoid adding a dependency in this change.
    "@typescript-eslint/naming-convention": [
      "warn",
      {
        selector: "default",
        format: ["camelCase"],
        leadingUnderscore: "allow",
        trailingUnderscore: "allow",
      },
      {
        selector: "variable",
        format: ["camelCase", "PascalCase", "UPPER_CASE"],
        leadingUnderscore: "allow",
      },
      {
        selector: "parameter",
        format: ["camelCase", "PascalCase"],
        leadingUnderscore: "allow",
      },
      {
        selector: "function",
        format: ["camelCase", "PascalCase"],
      },
      {
        selector: "typeLike",
        format: ["PascalCase"],
      },
      {
        selector: "enumMember",
        format: ["camelCase", "PascalCase", "UPPER_CASE"],
      },
      {
        selector: "property",
        format: null,
      },
      {
        selector: "import",
        format: ["camelCase", "PascalCase"],
      },
    ],
  },
  ignorePatterns: ["node_modules/", "dist/"],
};
