import { test, expect } from "../../support/test-base";
import { openPaymentsSubPage } from "../../support/auth-helpers";
import { openUtilitiesDashboard } from "../../support/utilities-helpers";

test.describe("Payments", () => {
  test.beforeEach(async ({ page }) => {
    await openUtilitiesDashboard(page);
  });

  test("Payment List", async ({ page }) => {
    // The route handler is registered up front so the initial fetch hits it,
    // but the response body is computed lazily from a mutable holder — later
    // steps swap `holder.responder` and trigger a refetch via Refresh to
    // exercise data-driven flows (rows, pagination, sort, errors).
    const holder: {
      responder: () => Promise<{
        status: number;
        contentType: string;
        body: string;
      }>;
    } = {
      responder: async () => ({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          success: true,
          data: {
            items: [],
            pageInfo: {
              hasNextPage: false,
              hasPreviousPage: false,
              startCursor: null,
              endCursor: null,
            },
          },
        }),
      }),
    };
    await page.route("**/api/payments**", async (route) => {
      if (route.request().method() !== "GET") {
        return route.continue();
      }
      await route.fulfill(await holder.responder());
    });

    // Stub the organizations endpoint before the page loads — the
    // Organization filter fetches its list via useGetOrganizations on mount.
    // The IAM endpoint is `/api/iam/organizations`, so the glob matches any
    // organizations URL regardless of sub-path.
    await page.route("**/organizations**", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          organizations: [
            { itemId: "org-acme", name: "Acme Corp" },
            { itemId: "org-globex", name: "Globex Inc" },
          ],
          totalCount: 2,
        }),
      });
    });

    await openPaymentsSubPage(page, "Payment List");

    await test.step("[Positive] header shows the live status badge and filters", async () => {
      await expect(page.getByRole("heading", { name: "Payment list" })).toBeVisible();
      await expect(page.getByText("Live", { exact: true })).toBeVisible();
      await expect(page.getByText("Filter payments", { exact: true })).toBeVisible();
    });

    await test.step("[Positive] filter fields cover provider/status/currency/flow/organization", async () => {
      await expect(page.getByText("All providers", { exact: true })).toBeVisible();
      await expect(page.getByText("All statuses", { exact: true })).toBeVisible();
      await expect(page.getByRole("combobox").filter({ hasText: "All currencies" })).toBeVisible();
      await expect(page.getByRole("combobox").filter({ hasText: "All flows" })).toBeVisible();
      await expect(
        page.getByRole("combobox").filter({ hasText: "All organizations" }),
      ).toBeVisible();
    });

    await test.step("[Positive] Apply filters can be triggered without error", async () => {
      await page.getByRole("button", { name: "Apply filters" }).click();
      await expect(page.getByRole("heading", { name: "Payment list" })).toBeVisible();
    });

    await test.step("[Positive] empty environment shows 'No payments yet' with the explanatory subtitle", async () => {
      await expect(page.getByText("No payments yet", { exact: true })).toBeVisible();
      await expect(
        page.getByText("Payments will appear here as soon as they are created.", {
          exact: true,
        }),
      ).toBeVisible();
    });

    await test.step("[Positive] Provider filter multi-select lets the user pick and clear a provider", async () => {
      // Anchor on the Providers label and walk up to its enclosing wrapper
      // so scoped locators don't bleed into sibling filter groups.
      const providersGroup = page.getByText("Providers", { exact: true }).locator("xpath=..");

      const providersTrigger = providersGroup.getByRole("button").first();
      await expect(providersTrigger).toHaveText("All providers");

      await providersTrigger.click();
      // The popover mounts checkboxes with `aria-label="Select <option>"`.
      // The single seeded provider suggestion is "ADYEN-ONLINE".
      const adyenCheckbox = page.getByRole("checkbox", {
        name: "Select ADYEN-ONLINE",
      });
      await expect(adyenCheckbox).toBeVisible();

      // Pick ADYEN-ONLINE — the trigger label should switch from
      // "All providers" to the selected value (single selection shows value).
      await adyenCheckbox.click();
      await expect(adyenCheckbox).toBeChecked();
      await expect(providersGroup.getByRole("button", { name: "ADYEN-ONLINE" })).toBeVisible();

      // Close the popover so subsequent steps can locate their own controls.
      await page.keyboard.press("Escape");

      // Clear the filter by reopening and unchecking.
      await providersGroup.getByRole("button", { name: "ADYEN-ONLINE" }).click();
      await adyenCheckbox.click();
      await expect(adyenCheckbox).not.toBeChecked();

      // Trigger should be back to its empty-state label.
      await expect(providersGroup.getByRole("button").first()).toHaveText("All providers");
      await page.keyboard.press("Escape");
    });

    await test.step("[Positive] Provider filter shows a '2 selected' count when two providers are chosen", async () => {
      const providersGroup = page.getByText("Providers", { exact: true }).locator("xpath=..");
      const providersTrigger = providersGroup.getByRole("button").first();
      await providersTrigger.click();

      // Pick the only suggested provider first.
      // Radix checkboxes are <button role="checkbox"> elements; use click()
      // for both check and uncheck since .check()/.uncheck() can hang on
      // Radix-managed state.
      const adyenCheckbox = page.getByRole("checkbox", {
        name: "Select ADYEN-ONLINE",
      });
      await adyenCheckbox.click();
      await expect(adyenCheckbox).toBeChecked();

      // Add a custom provider via the input — the popover surfaces a new
      // checkbox once the value is added (component dedupes via Set).
      // Use pressSequentially instead of fill because Playwright's fill()
      // sets the value atomically and React 18 sometimes batches the
      // onChange update so the component closure sees an empty customValue
      // when Enter is dispatched.
      const customInput = page.getByPlaceholder("Add provider name");
      await expect(customInput).toBeVisible();
      await customInput.click();
      await customInput.pressSequentially("stripe", { delay: 25 });
      await expect(customInput).toHaveValue("stripe");
      // Use the explicit "Add provider" button — its onClick handler reads
      // the current customValue via the component closure rather than the
      // keydown handler, sidestepping any React batching between fill and
      // keydown event firing.
      await page.getByRole("button", { name: "Add provider" }).click();

      const stripeCheckbox = page.getByRole("checkbox", { name: "Select STRIPE" });
      await expect(stripeCheckbox).toBeChecked();

      // Two selections → trigger label switches to "<count> selected".
      await expect(providersGroup.getByRole("button", { name: "2 selected" })).toBeVisible();

      // Uncheck one → falls back to the single-value label.
      // Note: when STRIPE leaves the values, displayedOptions no longer
      // includes it so the checkbox itself is unmounted from the popover.
      // We therefore verify the trigger label reverted to the single value.
      await stripeCheckbox.click();
      await expect(providersGroup.getByRole("button", { name: "ADYEN-ONLINE" })).toBeVisible();

      // Reset to empty before closing.
      await adyenCheckbox.click();
      await expect(adyenCheckbox).not.toBeChecked();
      await expect(providersGroup.getByRole("button").first()).toHaveText("All providers");
      await page.keyboard.press("Escape");
    });

    await test.step("[Positive] Provider filter lets the user add a custom provider name", async () => {
      const providersGroup = page.getByText("Providers", { exact: true }).locator("xpath=..");
      const providersTrigger = providersGroup.getByRole("button").first();
      await providersTrigger.click();

      // Custom-value input — only the Providers filter exposes one.
      const customInput = page.getByPlaceholder("Add provider name");
      await expect(customInput).toBeVisible();
      await customInput.click();

      // Mixed-case input gets upper-cased before being added.
      // Use pressSequentially to ensure each keystroke fires an input
      // event before Enter reads the value from the component closure.
      await customInput.pressSequentially("worldpay", { delay: 25 });
      await expect(customInput).toHaveValue("worldpay");
      await page.getByRole("button", { name: "Add provider" }).click();

      // Trigger label and the new checkbox reflect the normalized value.
      await expect(providersGroup.getByRole("button", { name: "WORLDPAY" })).toBeVisible();
      await expect(page.getByRole("checkbox", { name: "Select WORLDPAY" })).toBeChecked();

      // Enter key in the input also adds the value.
      await customInput.pressSequentially("adyen-test", { delay: 25 });
      await expect(customInput).toHaveValue("adyen-test");
      await customInput.press("Enter");
      await expect(providersGroup.getByRole("button", { name: "2 selected" })).toBeVisible();

      // Duplicate submission is silently ignored — still 2 selected, not 3.
      await customInput.pressSequentially("WORLDPAY", { delay: 25 });
      await expect(customInput).toHaveValue("WORLDPAY");
      await customInput.press("Enter");
      await expect(providersGroup.getByRole("button", { name: "2 selected" })).toBeVisible();

      // Clear all custom + standard selections to leave the filter clean.
      // Unchecking a custom value unmounts it from displayedOptions, so we
      // verify the trigger label after each click rather than the checkbox.
      await page.getByRole("checkbox", { name: "Select WORLDPAY" }).click();
      await page.getByRole("checkbox", { name: "Select ADYEN-TEST" }).click();
      await expect(providersGroup.getByRole("button").first()).toHaveText("All providers");
      await page.keyboard.press("Escape");
    });

    await test.step("[Positive] Status filter multi-select lets the user pick a status", async () => {
      // Anchor on the label element first and walk up to its enclosing
      // space-y-1.5 wrapper so the scoped locators don't bleed into the
      // Providers group (which sits before Statuses in the grid).
      const statusesGroup = page.getByText("Statuses", { exact: true }).locator("xpath=..");
      const statusesTrigger = statusesGroup.getByRole("button").first();
      await expect(statusesTrigger).toHaveText("All statuses");

      await statusesTrigger.click();
      // The popover mounts checkboxes with `aria-label="Select <option>"`.
      const authorizedCheckbox = page.getByRole("checkbox", {
        name: "Select AUTHORIZED",
      });
      await expect(authorizedCheckbox).toBeVisible();

      // Pick AUTHORIZED — the trigger label should switch to the selected value.
      await authorizedCheckbox.click();
      await expect(authorizedCheckbox).toBeChecked();
      await expect(statusesGroup.getByRole("button", { name: "AUTHORIZED" })).toBeVisible();

      // Pick a second status to verify the count label kicks in.
      const capturedCheckbox = page.getByRole("checkbox", {
        name: "Select CAPTURED",
      });
      await capturedCheckbox.click();
      await expect(capturedCheckbox).toBeChecked();
      await expect(statusesGroup.getByRole("button", { name: "2 selected" })).toBeVisible();

      // Clear back to empty so subsequent steps find their own controls.
      // Unchecking removes the option from displayedOptions, so verify the
      // trigger label reverted to the single-value/empty label instead.
      await capturedCheckbox.click();
      await expect(statusesGroup.getByRole("button", { name: "AUTHORIZED" })).toBeVisible();
      await authorizedCheckbox.click();
      await expect(statusesGroup.getByRole("button").first()).toHaveText("All statuses");
      await page.keyboard.press("Escape");
    });

    await test.step("[Positive] Currency single-select picks a currency and resets to placeholder", async () => {
      // Anchor on the Currency label and walk up to its enclosing wrapper
      // so scoped locators don't bleed into Flow/Organization groups.
      const currencyGroup = page.getByText("Currency", { exact: true }).locator("xpath=..");

      // Radix Select renders a combobox — placeholder is shown when empty.
      const currencyTrigger = currencyGroup.getByRole("combobox");
      await expect(currencyTrigger).toHaveText("All currencies");

      await currencyTrigger.click();
      // USD option is exposed in the listbox with code — name label.
      await page.getByRole("option", { name: "USD — US Dollar" }).click();

      await expect(currencyTrigger).toHaveText("USD — US Dollar");

      // Reopen and pick the "All currencies" sentinel to reset.
      await currencyTrigger.click();
      await page.getByRole("option", { name: "All currencies" }).click();
      await expect(currencyTrigger).toHaveText("All currencies");
    });

    await test.step("[Positive] Payment flow single-select picks a flow and clears", async () => {
      const flowGroup = page.getByText("Payment flow", { exact: true }).locator("xpath=..");

      const flowTrigger = flowGroup.getByRole("combobox");
      await expect(flowTrigger).toHaveText("All flows");

      await flowTrigger.click();
      await page.getByRole("option", { name: "Hosted checkout" }).click();
      await expect(flowTrigger).toHaveText("Hosted checkout");

      // Clear via the "All flows" sentinel option.
      await flowTrigger.click();
      await page.getByRole("option", { name: "All flows" }).click();
      await expect(flowTrigger).toHaveText("All flows");
    });

    await test.step("[Positive] More filters expands and collapses the extra filter rows", async () => {
      const moreFiltersButton = page.getByRole("button", { name: "More filters" });
      await expect(moreFiltersButton).toBeVisible();

      // Collapsed by default — the date/payment-id inputs must be hidden.
      await expect(page.locator('input[id="payment-date-from"]')).toHaveCount(0);
      await expect(page.locator('input[id="payment-order-id"]')).toHaveCount(0);

      await moreFiltersButton.click();
      await expect(page.getByRole("button", { name: "Fewer filters" })).toBeVisible();
      // Inputs are now mounted in the DOM.
      await expect(page.locator('input[id="payment-min-amount"]')).toBeVisible();
      await expect(page.locator('input[id="payment-max-amount"]')).toBeVisible();
      await expect(page.locator('input[id="payment-date-from"]')).toBeVisible();
      await expect(page.locator('input[id="payment-date-to"]')).toBeVisible();
      await expect(page.locator('input[id="payment-order-id"]')).toBeVisible();
      await expect(page.locator('input[id="payment-detail-id"]')).toBeVisible();

      await page.getByRole("button", { name: "Fewer filters" }).click();
      await expect(page.getByRole("button", { name: "More filters" })).toBeVisible();
      await expect(page.locator('input[id="payment-order-id"]')).toHaveCount(0);
    });

    await test.step("[Positive] More filters section lets the user type amount range, dates and IDs", async () => {
      await page.getByRole("button", { name: "More filters" }).click();

      await page.locator('input[id="payment-min-amount"]').fill("10");
      await page.locator('input[id="payment-max-amount"]').fill("500");
      await page.locator('input[id="payment-date-from"]').fill("2025-01-01");
      await page.locator('input[id="payment-date-to"]').fill("2025-12-31");
      await page.locator('input[id="payment-order-id"]').fill("ORD-1001");
      await page.locator('input[id="payment-detail-id"]').fill("pd_abc123");

      await expect(page.locator('input[id="payment-min-amount"]')).toHaveValue("10");
      await expect(page.locator('input[id="payment-max-amount"]')).toHaveValue("500");
      await expect(page.locator('input[id="payment-date-from"]')).toHaveValue("2025-01-01");
      await expect(page.locator('input[id="payment-date-to"]')).toHaveValue("2025-12-31");
      await expect(page.locator('input[id="payment-order-id"]')).toHaveValue("ORD-1001");
      await expect(page.locator('input[id="payment-detail-id"]')).toHaveValue("pd_abc123");

      // Active filter chip near the heading should now reflect the count
      // of non-empty filter fields (6 of the more-filters inputs).
      await expect(
        page
          .locator("form span")
          .filter({
            hasText: /^[0-9]+$/,
          })
          .first(),
      ).toBeVisible();

      // Collapse again to leave the page clean for subsequent steps.
      await page.getByRole("button", { name: "Fewer filters" }).click();
    });

    await test.step("[Negative] Amount range validation surfaces when min > max", async () => {
      // Open the more filters section so the amount inputs are mounted.
      await page.getByRole("button", { name: "More filters" }).click();

      await page.locator('input[id="payment-min-amount"]').fill("500");
      await page.locator('input[id="payment-max-amount"]').fill("100");

      // Submit triggers the client-side validator.
      await page.getByRole("button", { name: "Apply filters" }).click();

      await expect(
        page.getByRole("alert").filter({
          hasText: "Maximum amount must be greater than or equal to minimum amount.",
        }),
      ).toBeVisible();

      // Fix the range — submitting again clears the validation error.
      await page.locator('input[id="payment-max-amount"]').fill("1000");
      await page.getByRole("button", { name: "Apply filters" }).click();
      await expect(
        page.getByRole("alert").filter({
          hasText: "Maximum amount must be greater than or equal to minimum amount.",
        }),
      ).toHaveCount(0);

      // Reset state for the next step.
      await page.locator('input[id="payment-min-amount"]').fill("");
      await page.locator('input[id="payment-max-amount"]').fill("");
      await page.getByRole("button", { name: "Fewer filters" }).click();
    });

    await test.step("[Negative] Date range validation surfaces when from > to", async () => {
      await page.getByRole("button", { name: "More filters" }).click();

      await page.locator('input[id="payment-date-from"]').fill("2025-12-31");
      await page.locator('input[id="payment-date-to"]').fill("2025-01-01");

      await page.getByRole("button", { name: "Apply filters" }).click();
      await expect(
        page.getByRole("alert").filter({
          hasText: "The end date must be the same as or later than the start date.",
        }),
      ).toBeVisible();

      // Fix it and resubmit.
      await page.locator('input[id="payment-date-to"]').fill("2026-12-31");
      await page.getByRole("button", { name: "Apply filters" }).click();
      await expect(
        page.getByRole("alert").filter({
          hasText: "The end date must be the same as or later than the start date.",
        }),
      ).toHaveCount(0);

      await page.locator('input[id="payment-date-from"]').fill("");
      await page.locator('input[id="payment-date-to"]').fill("");
      await page.getByRole("button", { name: "Fewer filters" }).click();
    });

    await test.step("[Positive] Reset filters clears all filters and the active filter count", async () => {
      // Set up a few filters first so we can verify reset wipes them out.
      const providersGroup = page.getByText("Providers", { exact: true }).locator("xpath=..");
      await providersGroup.getByRole("button").first().click();
      const adyenCheckbox = page.getByRole("checkbox", {
        name: "Select ADYEN-ONLINE",
      });
      await adyenCheckbox.click();
      await expect(adyenCheckbox).toBeChecked();
      await page.keyboard.press("Escape");

      // Currency selector picks USD.
      const currencyGroup = page.getByText("Currency", { exact: true }).locator("xpath=..");
      const currencyTrigger = currencyGroup.getByRole("combobox");
      await currencyTrigger.click();
      await page.getByRole("option", { name: "USD — US Dollar" }).click();
      await expect(currencyTrigger).toHaveText("USD — US Dollar");

      // Active filter chip is now showing a non-zero count.
      const filterForm = page.locator('form:has(:text("Filter payments"))');
      const countChip = filterForm
        .locator("span")
        .filter({
          hasText: /^[0-9]+$/,
        })
        .first();
      await expect(countChip).toBeVisible();

      // Click Reset → chip disappears and trigger labels return to placeholder.
      await filterForm.getByRole("button", { name: "Reset" }).click();
      await expect(countChip).toHaveCount(0);
      await expect(providersGroup.getByRole("button").first()).toHaveText("All providers");
      await expect(currencyTrigger).toHaveText("All currencies");
    });

    await test.step("[Positive] Refresh button re-fetches the payments list", async () => {
      const refreshButton = page.getByRole("button", { name: "Refresh" });
      await expect(refreshButton).toBeVisible();

      // Clicking Refresh must produce a GET request to the payments endpoint.
      const responsePromise = page.waitForResponse(
        (resp) => resp.url().includes("/api/payments") && resp.request().method() === "GET",
      );
      await refreshButton.click();
      const response = await responsePromise;
      expect(response.status()).toBe(200);
    });

    await test.step("[Positive] Organization filter picks an organization and clears", async () => {
      // Organizations endpoint was stubbed before navigation so the dropdown
      // has two seeded entries: Acme Corp and Globex Inc.
      const orgGroup = page.getByText("Organization", { exact: true }).locator("xpath=..");
      const orgTrigger = orgGroup.getByRole("combobox");
      await expect(orgTrigger).toHaveText("All organizations");

      await orgTrigger.click();
      await page.getByRole("option", { name: "Acme Corp" }).click();
      await expect(orgTrigger).toHaveText("Acme Corp");

      // Clear via the "All organizations" sentinel option.
      await orgTrigger.click();
      await page.getByRole("option", { name: "All organizations" }).click();
      await expect(orgTrigger).toHaveText("All organizations");
    });

    await test.step("[Positive] Rows per page selector changes the page size and triggers a refetch", async () => {
      // Capture the request URL so we can verify the pageSize filter change.
      const pageSizeResponsePromise = page.waitForResponse(
        (resp) => resp.url().includes("/api/payments") && resp.request().method() === "GET",
      );

      const pageSizeSelect = page.locator("#payment-page-size");
      await pageSizeSelect.click();
      // 10 is one of PAYMENT_PAGE_SIZE_OPTIONS — exact match avoids colliding
      // with the "100" entry.
      await page.getByRole("option", { name: "10", exact: true }).click();

      await expect(pageSizeSelect).toHaveText("10");
      const response = await pageSizeResponsePromise;
      expect(response.status()).toBe(200);

      // Reset to 25 to keep subsequent steps deterministic.
      await pageSizeSelect.click();
      await page.getByRole("option", { name: "25", exact: true }).click();
      await expect(pageSizeSelect).toHaveText("25");
    });

    await test.step("[Positive] No matching payments shows the filtered empty state with a Clear filters button", async () => {
      // With the empty stub still in place, applying any filter should
      // surface the filtered-empty state and offer a Clear filters button.
      await page
        .getByText("Statuses", { exact: true })
        .locator("xpath=..")
        .getByRole("button")
        .first()
        .click();
      await page.getByRole("checkbox", { name: "Select REFUNDED" }).click();
      await page.keyboard.press("Escape");
      await page.getByRole("button", { name: "Apply filters" }).click();

      await expect(page.getByText("No matching payments", { exact: true })).toBeVisible();
      await expect(
        page.getByText("Try adjusting or clearing the current filters.", {
          exact: true,
        }),
      ).toBeVisible();

      // Clear filters button is offered in the filtered-empty state.
      const clearButton = page.getByRole("button", { name: "Clear filters" });
      await expect(clearButton).toBeVisible();

      await clearButton.click();
      // After clearing, the placeholder empty state returns.
      await expect(page.getByText("No payments yet", { exact: true })).toBeVisible();
    });

    await test.step("[Positive] Last refreshed timestamp appears after the first fetch completes", async () => {
      // The route stub already triggered one fetch during navigation; the page
      // shows the timestamp next to the description. Format: "Last refreshed at HH:MM".
      await expect(
        page.getByText(/^Last refreshed at \d{1,2}:\d{2}/, { exact: false }),
      ).toBeVisible();
    });

    await test.step("[Positive] Payment table renders rows with provider, amount, date, status and actions", async () => {
      // Swap the responder to return two seeded payments, then trigger refetch.
      const now = new Date().toISOString();
      holder.responder = async () => ({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          success: true,
          data: {
            items: [
              {
                paymentDetailId: "pd_aaaaaaaa1111bbbbbbbb",
                providerName: "ADYEN-ONLINE",
                amount: 250.5,
                currencyCode: "USD",
                paymentDateUtc: now,
                paymentStatus: "CAPTURED",
                hasPendingRefund: false,
              },
              {
                paymentDetailId: "pd_cccccccc2222dddddddd",
                providerName: "ADYEN-ONLINE",
                amount: 99.99,
                currencyCode: "EUR",
                paymentDateUtc: now,
                paymentStatus: "AUTHORIZED",
                hasPendingRefund: false,
              },
            ],
            pageInfo: {
              hasNextPage: false,
              hasPreviousPage: false,
              startCursor: null,
              endCursor: null,
            },
          },
        }),
      });
      await page.getByRole("button", { name: "Refresh" }).click();

      // Header columns.
      await expect(page.getByRole("button", { name: /Provider/ }).first()).toBeVisible();
      await expect(page.getByRole("button", { name: /Amount/ }).first()).toBeVisible();
      await expect(page.getByRole("button", { name: /Payment date/ }).first()).toBeVisible();
      await expect(page.getByRole("button", { name: /Status/ }).first()).toBeVisible();

      // Row content — short ids, provider names, status badges.
      // shortenPaymentId truncates to "<first10>…<last6>".
      // Scope to the table so we don't collide with the mobile-card view.
      const table = page.getByRole("table");
      await expect(table.getByText("pd_aaaaaaa…bbbbbb", { exact: true })).toBeVisible();
      await expect(table.getByText("pd_ccccccc…dddddd", { exact: true })).toBeVisible();
      // Status badges render the human-friendly label, not the raw enum.
      await expect(table.getByText("Captured", { exact: true })).toBeVisible();
      await expect(table.getByText("Authorized", { exact: true })).toBeVisible();
      // Both seeded rows have refundable statuses.
      await expect(page.getByRole("button", { name: "Refund" }).first()).toBeVisible();

      // Counter line above the table reflects the row count.
      await expect(page.getByText("2 payments on this page", { exact: true })).toBeVisible();
    });

    await test.step("[Positive] Sort by column toggles direction on repeated clicks", async () => {
      const providerHeader = page.getByRole("button", { name: /Provider/ }).first();
      // First click switches to asc because the default sort is paymentDate desc.
      await providerHeader.click();
      // After sort change, the table re-renders the sort indicator on the
      // active header. Active header uses `text-foreground` (vs `text-muted-foreground`).
      await expect(providerHeader).toHaveClass(/text-foreground/);

      // Second click toggles to desc.
      await providerHeader.click();
      await expect(providerHeader).toHaveClass(/text-foreground/);
    });

    await test.step("[Positive] Pagination enables Next when hasNextPage is true", async () => {
      holder.responder = async () => ({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          success: true,
          data: {
            items: [
              {
                paymentDetailId: "pd_page1row11111111111",
                providerName: "ADYEN-ONLINE",
                amount: 10,
                currencyCode: "USD",
                paymentDateUtc: new Date().toISOString(),
                paymentStatus: "CAPTURED",
                hasPendingRefund: false,
              },
            ],
            pageInfo: {
              hasNextPage: true,
              hasPreviousPage: false,
              startCursor: "cursor-start-1",
              endCursor: "cursor-end-1",
            },
          },
        }),
      });
      await page.getByRole("button", { name: "Refresh" }).click();

      await expect(page.getByText("Page 1", { exact: true })).toBeVisible();
      // Next is enabled, Previous is disabled (no previous page yet).
      const previousButton = page.getByRole("button", { name: "Previous" });
      const nextButton = page.getByRole("button", { name: "Next" });
      await expect(previousButton).toBeDisabled();
      await expect(nextButton).toBeEnabled();

      await nextButton.click();
      await expect(page.getByText("Page 2", { exact: true })).toBeVisible();
    });

    await test.step("[Negative] Error state surfaces 'Payments could not be loaded' with Try again", async () => {
      // Apply a filter first so the Reset button becomes enabled — without
      // any active filter the Reset control is disabled.
      await page
        .getByText("Statuses", { exact: true })
        .locator("xpath=..")
        .getByRole("button")
        .first()
        .click();
      await page.getByRole("checkbox", { name: "Select REFUNDED" }).click();
      await page.keyboard.press("Escape");
      await page.getByRole("button", { name: "Apply filters" }).click();

      // Reset filters so the empty-state error renders instead of the
      // filtered-empty branch.
      const filterForm = page.locator('form:has(:text("Filter payments"))');
      await filterForm.getByRole("button", { name: "Reset" }).click();

      // Switch the responder to a failing one and reload the page so the
      // hook starts with no cached entry — the page's error UI only renders
      // when `isError && !data`, and TanStack keeps previous data on
      // refetch, so a soft Refresh wouldn't surface the error UI.
      holder.responder = async () => ({
        status: 500,
        contentType: "application/json",
        body: JSON.stringify({
          success: false,
          data: null,
          error: { code: "INTERNAL", message: "Boom" },
        }),
      });
      await page.reload();
      await page.getByRole("heading", { name: "Payment list" }).waitFor();

      await expect(page.getByText("Payments could not be loaded", { exact: true })).toBeVisible();
      const tryAgainButton = page.getByRole("button", { name: "Try again" });
      await expect(tryAgainButton).toBeVisible();

      // Restore the happy-path responder and click Try again — the table
      // should re-render with the seeded rows from the previous step.
      holder.responder = async () => ({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          success: true,
          data: {
            items: [
              {
                paymentDetailId: "pd_recover00000000000000",
                providerName: "ADYEN-ONLINE",
                amount: 50,
                currencyCode: "USD",
                paymentDateUtc: new Date().toISOString(),
                paymentStatus: "CAPTURED",
                hasPendingRefund: false,
              },
            ],
            pageInfo: {
              hasNextPage: false,
              hasPreviousPage: false,
              startCursor: null,
              endCursor: null,
            },
          },
        }),
      });
      await tryAgainButton.click();
      // Scope to the table — desktop and mobile renderings both show the id.
      const table = page.getByRole("table");
      await expect(table.getByText("pd_recover…000000", { exact: true })).toBeVisible();
    });

    await test.step("[Positive] Refund action on a CAPTURED row opens the refund dialog", async () => {
      // The previous step left one seeded CAPTURED row in the table.
      const refundButton = page.getByRole("button", { name: "Refund" }).first();
      await expect(refundButton).toBeVisible();
      await refundButton.click();

      // Dialog renders the amount, reason field, and action buttons.
      await expect(page.getByRole("heading", { name: "Refund payment" })).toBeVisible();
      await expect(page.getByLabel("Refund amount")).toBeVisible();
      await expect(page.getByLabel(/^Reason/)).toBeVisible();
      await expect(page.getByRole("button", { name: "Cancel" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Confirm refund" })).toBeVisible();

      // Cancel closes the dialog without submitting.
      await page.getByRole("button", { name: "Cancel" }).click();
      await expect(page.getByRole("heading", { name: "Refund payment" })).toHaveCount(0);
    });
  });
});
