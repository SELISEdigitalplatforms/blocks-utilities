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
    "prettier",
  ],
  rules: {
    // Confirmed false positives, disabled with justification (see CONTRIBUTING.md):
    // - react/prop-types is redundant in this TypeScript codebase; props are
    //   type-checked by the compiler, so the rule only fires on typed components.
    // - react/no-unknown-property allows the cmdk library's DOM data attribute.
    "react/no-unknown-property": ["error", { ignore: ["cmdk-input-wrapper"] }],
    "react/prop-types": "off",
    "react/react-in-jsx-scope": "off",
    "react/jsx-uses-react": "off",
    "no-unused-vars": "off",
    "no-console": ["error", { allow: ["error"] }],
    // User-facing text goes through the i18n runtime with English keys in client/app/i18n/<module>.en.json;
    // other languages come from the localization service. Flags JSX text and text attributes with at least two
    // lowercase letters (acronyms such as "SCA" pass). Genuinely untranslatable tokens use a targeted disable.
    "no-restricted-syntax": [
      "error",
      {
        selector: "JSXText[value=/[a-z]{2,}/]",
        message:
          "User-facing text must use an i18n key (useT/tStatic) in the module's .en.json, not a hard-coded string.",
      },
      {
        selector:
          "JSXAttribute[name.name=/^(placeholder|title|alt|aria-label)$/] > Literal[value=/[a-z]{2,}/]",
        message:
          "User-facing text must use an i18n key (useT/tStatic) in the module's .en.json, not a hard-coded string.",
      },
      {
        selector:
          "JSXAttribute[name.name=/^(placeholder|title|alt|aria-label)$/] > JSXExpressionContainer > TemplateLiteral[expressions.length=0][quasis.0.value.raw=/[a-z]{2,}/]",
        message:
          "User-facing text must use an i18n key (useT/tStatic) in the module's .en.json, not a hard-coded string.",
      },
    ],
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
    // Naming conventions. Kept intentionally permissive to match the code that exists today
    // (see CONTRIBUTING.md for the target conventions); ratchet toward stricter formats over time.
    // Object/property/import names are left unchecked because they mirror server wire fields.
    "@typescript-eslint/naming-convention": [
      "warn",
      {
        selector: "variableLike",
        format: ["camelCase", "PascalCase", "UPPER_CASE"],
        leadingUnderscore: "allow",
      },
      {
        selector: "typeLike",
        format: ["PascalCase"],
      },
      {
        // Enums in this repo use both PascalCase and UPPER_CASE names; allow both for now.
        selector: "enum",
        format: ["PascalCase", "UPPER_CASE"],
      },
      {
        selector: "interface",
        format: ["PascalCase"],
        // Some interfaces use an I prefix, some do not; do not force either yet.
      },
      {
        selector: ["property", "objectLiteralProperty", "typeProperty", "enumMember"],
        format: null,
      },
      {
        selector: "import",
        format: null,
      },
    ],
  },
  overrides: [
    {
      // Tests, stories and test helpers may use literal text.
      files: [
        "**/*.test.{ts,tsx}",
        "**/*.spec.{ts,tsx}",
        "**/*.stories.{ts,tsx}",
        "**/__tests__/**",
        "**/__mocks__/**",
        "**/test-utils/**",
      ],
      rules: { "no-restricted-syntax": "off" },
    },
  ],
  ignorePatterns: ["node_modules/", "dist/"],
};
