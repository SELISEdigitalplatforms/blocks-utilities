import test from "@playwright/test";
import {
  openConsoleWithNotifications,
  switchTheme,
  verifyLanguageSelector,
  verifyNotificationMarksReadOnHover,
  verifyNotificationBellPopover,
  verifyAppSwitcher,
  verifyUserAvatarMenu,
} from "../../page/overview/topbar";
import {
  verifyProjectList,
  verifyProjectSettingsCrossAppNavigation,
  verifyResourceCardsNavigate,
} from "../../page/overview/console";
import {
  openDashboard,
  verifyOverviewSidebarLink,
  verifyWorkspaceWidgets,
  verifyProjectDetailsCard,
  verifyXBlocksKeyMaskAndCopy,
  verifyCoreApisExpansion,
  verifyCopyAsCurl,
} from "../../page/overview/dashboard";

test.describe("flow: Overview menu", () => {
  test("Overview page — console, topbar, sidebar navigation, Project Details, Core APIs", async ({
    page,
  }) => {
    test.setTimeout(150_000);

    await openConsoleWithNotifications(page);

    await test.step("Topbar: switching theme to Dark applies it, then Light restores it", () =>
      switchTheme(page));
    await test.step("Topbar: language selector lists EN/German/French with non-English disabled", () =>
      verifyLanguageSelector(page));
    await test.step("Topbar: an unread notification is marked read on hover (not requiring a click)", () =>
      verifyNotificationMarksReadOnHover(page));
    await test.step("Topbar: notification bell opens the popover and 'Mark all as read' is usable", () =>
      verifyNotificationBellPopover(page));
    await test.step("Topbar: app switcher opens the SELISE Blocks apps list", () =>
      verifyAppSwitcher(page));
    await test.step("Topbar: user avatar menu lists 'My Profile' and 'Log out', and Profile navigates", () =>
      verifyUserAvatarMenu(page));

    await test.step("Console: project list shows a real project card with name and environment chip", () =>
      verifyProjectList(page));
    await test.step("Console: project card's settings icon navigates cross-app to the environments overview (Blocks OS)", () =>
      verifyProjectSettingsCrossAppNavigation(page));
    await test.step("Console: Resources cards (Docs/Code/Cloud) actually navigate to their target URL when clicked", () =>
      verifyResourceCardsNavigate(page));

    const dashboardUrl = await openDashboard(page);

    await test.step("'Overview' sidebar link is itemId-scoped and actually navigates back here", () =>
      verifyOverviewSidebarLink(page, dashboardUrl));
    await test.step("Workspace area (sidebar): Project/Environment widgets show current context and are permanently disabled", () =>
      verifyWorkspaceWidgets(page));
    await test.step("Project Details card shows Name, X-Blocks-Key, and a human-readable Environment badge", () =>
      verifyProjectDetailsCard(page));
    await test.step("X-Blocks-Key is masked, and its hover-reveal copy button works", () =>
      verifyXBlocksKeyMaskAndCopy(page));
    await test.step("Core APIs card lists endpoint groups, collapsed by default, and expands on click", () =>
      verifyCoreApisExpansion(page));
    await test.step("'Copy as cURL' on an endpoint is hover-reveal and copies something to the clipboard", () =>
      verifyCopyAsCurl(page));
  });
});
