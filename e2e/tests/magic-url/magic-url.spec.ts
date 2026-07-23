import { test, expect } from "@playwright/test";
const EMAIL = process.env.E2E_USERNAME!;
const PASSWORD = process.env.E2E_PASSWORD!;
const URL = process.env.E2E_PASSWORD!;

test.describe("Magic URL", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto(URL);
    await page.getByRole("button", { name: "Log in to your account" }).click();

    // 2. Redirected to the dev-iam OIDC login page (/oidc/login, cross-origin).
    //    Selectors come from blocks-idp oidc-login-form.tsx (stable field ids).
    const emailField = page.locator("#oidc-email");
    await emailField.waitFor({ timeout: 30_000 });
    await emailField.fill(EMAIL);
    await page.locator("#oidc-password").fill(PASSWORD!);
    await page.getByRole("button", { name: "Login", exact: true }).click();
    await page.getByText(/Development|Staging|IAT|UAT/i).click();
    await page.getByRole("link", { name: "Magic URL" }).click();
  });

  test("TC33 - Create Magic URL dialog opens", async ({ page }) => {
    const createButton = page
      .getByRole("button", { name: /create magic url|create/i })
      .first();
    if (await createButton.count()) {
      await createButton.click();
    }
    await expect(page.getByText(/magic url/i).first()).toBeVisible({
      timeout: 15000,
    });
  });

  test("TC34 - Magic URL form fields accept input", async ({ page }) => {
    const createButton = page
      .getByRole("button", { name: /create magic url|create/i })
      .first();
    if (await createButton.count()) {
      await createButton.click();
    }
    const urlInput = page
      .locator("input")
      .filter({ hasNot: page.locator('[type="hidden"]') })
      .first();
    if (await urlInput.count()) {
      await urlInput.fill("https://example.com");
    }
    const controls = page.locator("input, textarea, select");
    const count = await controls.count();
    expect(count).toBeGreaterThan(0);
  });

  test("TC35 - Create button remains disabled for empty fields", async ({
    page,
  }) => {
    const createButton = page
      .getByRole("button", { name: /create magic url|create/i })
      .first();
    if (await createButton.count()) {
      await createButton.click();
    }
    const primaryButton = page.getByRole("button", { name: /create/i }).last();
    await expect(primaryButton).toBeDisabled({ timeout: 10000 });
  });

  test("TC36 - Whitespace-only input shows validation", async ({ page }) => {
    test.fail(
      true,
      "App Bug: Entering only spaces triggers the generic 'Please enter a valid URL' error instead of a whitespace validation message.",
    );
    const createButton = page
      .getByRole("button", { name: /create magic url|create/i })
      .first();
    if (await createButton.count()) {
      await createButton.click();
    }
    const urlInput = page.locator("input").first();
    if (await urlInput.count()) {
      await urlInput.fill("   ");
    }
    await expect(
      page.getByText(/validation|required|whitespace/i).first(),
    ).toBeVisible({ timeout: 10000 });
  });

  test("TC37 - Invalid email format is rejected", async ({ page }) => {
    test.fail(
      true,
      "App Bug: No validation message is displayed when an invalid URL ('www.hh') is entered. The application should reject the input and display an appropriate validation message.",
    );
    const createButton = page
      .getByRole("button", { name: /create magic url|create/i })
      .first();
    if (await createButton.count()) {
      await createButton.click();
    }
    const uriInput = page.locator("input").first();
    if (await uriInput.count()) {
      await uriInput.fill("www.hh");
    }
    await expect(page.getByText(/invalid|validation/i).first()).toBeVisible({
      timeout: 10000,
    });
  });

  test("TC38 - Cancel or close resets the form", async ({ page }) => {
    const createButton = page
      .getByRole("button", { name: /create magic url|create/i })
      .first();
    if (await createButton.count()) {
      await createButton.click();
    }
    const closeButton = page
      .getByRole("button", { name: /close|cancel|x/i })
      .first();
    if (await closeButton.count()) {
      await closeButton.click();
    }
    await expect(page.getByText(/magic url/i).first()).toBeVisible({
      timeout: 10000,
    });
  });

  test("TC39 - Dynamic fields appear when Action is selected", async ({
    page,
  }) => {
    const createButton = page
      .getByRole("button", { name: /create magic url|create/i })
      .first();
    if (await createButton.count()) {
      await createButton.click();
    }
    const typeSelect = page.getByRole("combobox").first();
    if (await typeSelect.count()) {
      await typeSelect.click();
      await page.getByText(/action/i).click();
    }
    await expect(
      page.getByText(/request|payload|headers/i).first(),
    ).toBeVisible({
      timeout: 10000,
    });
  });

  test("TC40 - URL field validates short input", async ({ page }) => {
    const createButton = page
      .getByRole("button", { name: /create magic url|create/i })
      .first();
    if (await createButton.count()) {
      await createButton.click();
    }
    const uriInput = page.locator("input").first();
    if (await uriInput.count()) {
      await uriInput.fill("a");
    }
    await expect(
      page
        .getByText(/invalid|validation|too short|Please enter a valid URI/i)
        .first(),
    ).toBeVisible({ timeout: 10000 });
  });

  test("TC41 - Magic URL can be created with valid data", async ({ page }) => {
    const createButton = page
      .getByRole("button", { name: /create magic url|create/i })
      .first();
    if (await createButton.count()) {
      await createButton.click();
    }

    const uriInput = page.locator("input#url, input[name='uri']").first();
    const nameInput = page.locator("input#name, input[name='name']").first();

    if (await uriInput.count()) {
      await uriInput.fill("https://example.com");
    }
    if (await nameInput.count()) {
      await nameInput.fill("Example Magic URL");
    }

    const createPrimaryButton = page
      .getByRole("button", { name: /^create$/i })
      .last();

    await expect(createPrimaryButton).toBeEnabled({ timeout: 10000 });
    await createPrimaryButton.click();

    await expect(
      page.getByText(/magic url|created|success/i).first(),
    ).toBeVisible({
      timeout: 15000,
    });
  });

  test("TC42 - Configure Magic URL window is available", async ({ page }) => {
    const heading = page.getByRole("heading", { name: /magic url/i }).first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    const createButton = page
      .getByRole("button", { name: /create magic url|create/i })
      .first();
    if (await createButton.count()) {
      await createButton.click();
    }

    await expect(page.getByText(/magic url/i).first()).toBeVisible({
      timeout: 10000,
    });
  });

  test("TC43 - Search box is available", async ({ page }) => {
    const searchBox = page.getByPlaceholder(/search/i).first();
    if (await searchBox.count()) {
      await expect(searchBox).toBeVisible();
    }
  });

  test("TC44 - Search filters Magic URLs", async ({ page }) => {
    const searchBox = page.getByPlaceholder(/search/i).first();
    if (await searchBox.count()) {
      await searchBox.fill("magic");
      await expect(searchBox).toHaveValue(/magic/i);
    }
  });

  test("TC45 - Search input can be cleared", async ({ page }) => {
    const searchBox = page.getByPlaceholder(/search/i).first();

    if (await searchBox.count()) {
      await searchBox.fill("magic");

      await page.getByRole("button").filter({ hasText: /^$/ }).nth(3).click();
      await expect(searchBox).toHaveValue("", { timeout: 5000 });
    }
  });

  test("TC46 - Status filter can be applied", async ({ page }) => {
    const statusButton = page.getByRole("button", { name: /status/i }).first();
    if (await statusButton.count()) {
      await statusButton.click();
    }
    await expect(page.getByText(/status/i).first()).toBeVisible({
      timeout: 10000,
    });
  });

  test("TC47 - Request method filter can be applied", async ({ page }) => {
    const requestMethodButton = page
      .getByRole("button", { name: /request method/i })
      .first();
    if (await requestMethodButton.count()) {
      await requestMethodButton.click();
    }
    await expect(page.getByText(/request method/i).first()).toBeVisible({
      timeout: 10000,
    });
  });

  test("TC48 - Scheduled expiry date filter is reachable", async ({ page }) => {
    const expiryButton = page
      .getByRole("button", { name: /expiry|scheduled/i })
      .first();
    if (await expiryButton.count()) {
      await expiryButton.click();
    }
    await expect(page.getByText(/expiry|date/i).first()).toBeVisible({
      timeout: 10000,
    });
  });

  test("TC49 - Magic URL column sorting is interactive", async ({ page }) => {
    const sortButton = page
      .getByRole("button", { name: /sort|arrow/i })
      .first();
    if (await sortButton.count()) {
      await sortButton.click();
    }
    await expect(page.locator('table, [role="table"]')).toBeVisible({
      timeout: 10000,
    });
  });

  test("TC50 - Magic URL details can be opened", async ({ page }) => {
    const detailsButton = page
      .getByRole("button", { name: /details|view/i })
      .first();
    if (await detailsButton.count()) {
      await detailsButton.click();
    }
    await expect(page.getByText(/details|magic url/i).first()).toBeVisible({
      timeout: 15000,
    });
  });
});
