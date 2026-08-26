import { useMemo } from "react";
import { useLocation, useSearchParams } from "react-router";
import { DashboardRoute } from "@seliseblocks/genesis-os/layouts";
import { ORGANIZATION_QUERY_PARAM } from "@/cross-modules/utilities/subscription/constants/subscription.constants";
import { withSubscriptionOrganizationScope } from "@/cross-modules/utilities/subscription/utilities/subscription-navigation";
import type { Menu } from "@/models/menu-models";

/**
 * The dashboard shell, with the sidebar's subscription links kept on the organization in view.
 *
 * Wrapped here rather than solved inside the subscription module because the sidebar is rendered by
 * the shell, above every page: a page cannot reach the links that navigate away from it. This is the
 * one place both the menu tree and the current URL are in hand.
 *
 * Nothing else about the shell changes, and no other module's links are touched — see
 * `withSubscriptionOrganizationScope`.
 */
export const ScopedDashboardRoute = ({
  navigationMenus,
  redirectPaths,
}: {
  navigationMenus: Menu[];
  redirectPaths: Record<string, string>;
}) => {
  const [searchParams] = useSearchParams();
  const { pathname } = useLocation();
  const organizationId = searchParams.get(ORGANIZATION_QUERY_PARAM);

  const menus = useMemo(
    () => withSubscriptionOrganizationScope(navigationMenus, organizationId, pathname),
    [navigationMenus, organizationId, pathname],
  );

  return <DashboardRoute redirectPaths={redirectPaths} navigationMenus={menus} />;
};
