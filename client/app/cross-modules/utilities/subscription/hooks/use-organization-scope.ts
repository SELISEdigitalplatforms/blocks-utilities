import { useSearchParams } from "react-router";
import { ORGANIZATION_QUERY_PARAM } from "../constants/subscription.constants";

/**
 * The organization whose catalogue the portal is currently showing, or undefined for tenant-wide.
 *
 * Lives in the URL rather than component state because a plan scoped to an organization is
 * invisible without it: the server resolves the portal's own context to the console
 * organization, so reading such a plan needs this named explicitly. Keeping it in the URL means
 * a refresh, a back button or a pasted link all still land on a plan that exists.
 */
export const useOrganizationScope = (): string | undefined => {
  const [searchParams] = useSearchParams();

  return searchParams.get(ORGANIZATION_QUERY_PARAM) ?? undefined;
};

/** Carries the current organization scope onto another link within the portal. */
export const withOrganizationScope = (
  path: string,
  organizationId: string | null | undefined,
): string =>
  organizationId
    ? `${path}?${ORGANIZATION_QUERY_PARAM}=${encodeURIComponent(organizationId)}`
    : path;
