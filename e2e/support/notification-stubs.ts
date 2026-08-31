import type { Page } from "@playwright/test";

const E2E_NOTIFICATION_ID = "e2e-notification-hover";

function notificationFeedResponse(isRead: boolean) {
  return {
    unReadNotificationsCount: isRead ? 0 : 1,
    totalNotificationsCount: 1,
    notifications: [
      {
        id: E2E_NOTIFICATION_ID,
        isRead,
        createdTime: new Date().toISOString(),
        denormalizedPayload: JSON.stringify({
          title: "e2e_hover_read",
          description: "Hover should mark this notification as read",
          redirectPath: "",
          toastable: false,
          meta: "",
        }),
      },
    ],
  };
}

/**
 * Stub the Blocks Notifier API so notification topbar tests have a deterministic
 * unread row even when the shared e2e account has an empty real feed.
 */
export async function stubNotificationFeed(page: Page): Promise<void> {
  await page.route("**/api/Notifier/GetNotifications**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(notificationFeedResponse(false)),
    });
  });

  await page.route("**/api/Notifier/MarkNotificationAsRead**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ errors: null, isSuccess: true }),
    });
  });

  await page.route("**/api/Notifier/MarkAllNotificationAsRead**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ errors: null, isSuccess: true }),
    });
  });
}

export function notificationRows(page: Page) {
  const panel = page
    .locator('[data-radix-popper-content-wrapper]')
    .filter({ has: page.getByText("Notifications", { exact: true }) });

  return panel.locator(
    '[class*="cursor-pointer"][class*="items-start"][class*="border-b"]',
  );
}
