import type { Menu } from "@/models/menu-models";
import { ORGANIZATION_QUERY_PARAM } from "../constants/subscription.constants";

/**
 * The prefix every subscription entry in the navigation menu carries, as authored.
 *
 * Matched before the shell rewrites `/app/` to `/app/{itemId}/`, which is the only point at which a
 * subscription path is still distinguishable from any other module's by its literal prefix.
 */
export const SUBSCRIPTION_MENU_PREFIX = "/app/subscription";

/** A menu path with its `/app` root removed, which is what a rewritten pathname still ends with. */
const routeOf = (menuPath: string): string => menuPath.slice("/app".length);

/**
 * The same, for a live pathname: `/app/{itemId}/subscription/plans` becomes `/subscription/plans`.
 *
 * Dropping the project segment rather than reconstructing it, because the shell inserts it and this
 * only needs the two to be comparable.
 */
const routeOfPathname = (pathname: string): string => {
  const [, root, , ...rest] = pathname.split("/");

  return root === "app" ? `/${rest.join("/")}` : pathname;
};

/**
 * Carries the current organization scope onto the subscription entries of a navigation menu.
 *
 * The scope lives in the URL and the shell's sidebar links are plain paths, so every trip through
 * the sidebar used to drop it. Somebody would pick organization A on the plan list, click Billing
 * profile, and edit whichever organization the server resolves for an unscoped request — usually the
 * console's own. Nothing on the screen said so, which is what made it confusing rather than merely
 * wrong.
 *
 * Only paths under {@link SUBSCRIPTION_MENU_PREFIX} are touched. The scope is this module's own idea
 * and means nothing to Payment or IAM, so putting it on their links would advertise a filter they do
 * not apply. Every subscription entry gets it, including the ones that ignore it — a menu rule with
 * named exceptions is how the next entry added comes to be forgotten.
 *
 * @param pathname
 * The current location. An entry the page is already inside is left alone, because the sidebar marks
 * an entry active by testing `pathname.startsWith(menu.path)`, and a query string appended there
 * matches nothing — the active item would quietly stop being highlighted. Nothing is lost: the link
 * left clean is the one that navigates nowhere.
 */
export const withSubscriptionOrganizationScope = (
  menus: Menu[],
  organizationId: string | null | undefined,
  pathname: string,
): Menu[] => {
  if (!organizationId) {
    return menus;
  }

  const current = routeOfPathname(pathname);

  const scoped = (menu: Menu): Menu => {
    if (menu.type !== "menu") {
      return menu;
    }

    const withChildren = menu.children
      ? { ...menu, children: menu.children.map(scoped) }
      : menu;

    if (
      !menu.path.startsWith(SUBSCRIPTION_MENU_PREFIX) ||
      current.startsWith(routeOf(menu.path))
    ) {
      return withChildren;
    }

    const separator = menu.path.includes("?") ? "&" : "?";

    return {
      ...withChildren,
      path: `${menu.path}${separator}${ORGANIZATION_QUERY_PARAM}=${encodeURIComponent(
        organizationId,
      )}`,
    };
  };

  return menus.map(scoped);
};
