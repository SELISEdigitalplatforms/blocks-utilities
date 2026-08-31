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
  let isRead = false;

  // Prevent SignalR/socket listeners from invalidating the feed mid-assertion.
  await page.route("**/api/Notification/Gets**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        configurations: [],
        totalCount: 0,
        errors: null,
        isSuccess: true,
      }),
    });
  });

  await page.route("**/GetNotifications**", async (route) => {
    if (!route.request().url().includes("Notifier")) {
      await route.continue();
      return;
    }
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(notificationFeedResponse(isRead)),
    });
  });

  await page.route("**/MarkNotificationAsRead**", async (route) => {
    isRead = true;
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ errors: null, isSuccess: true }),
    });
  });

  await page.route("**/MarkAllNotificationAsRead**", async (route) => {
    isRead = true;
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ errors: null, isSuccess: true }),
    });
  });
}

export function notificationRows(page: Page) {
  return page
    .locator('[class*="cursor-pointer"][class*="items-start"][class*="border-b"]')
    .filter({ hasText: "E2e Hover Read" });
}