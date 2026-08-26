import { useCallback } from "react";
import { useParams } from "react-router";
import { useOrganizationScope, withOrganizationScope } from "./use-organization-scope";

/**
 * Builds a link to another subscription screen, keeping the organization currently being looked at.
 *
 * One helper rather than a template string per page, because the path has two moving parts — the
 * project segment the shell owns and the organization scope this module owns — and every page that
 * spelled them out by hand got one of them wrong. Three of them pointed at `/dashboard/subscription`,
 * a route that does not exist.
 *
 * @returns
 * A builder taking the part after `/subscription/`. The organization defaults to the current scope;
 * pass one explicitly to link into a different organization, or `null` to link out of the scope
 * deliberately.
 */
export const useSubscriptionLink = (): ((
  suffix: string,
  organizationId?: string | null,
) => string) => {
  const { itemId } = useParams();
  const scope = useOrganizationScope();

  return useCallback(
    (suffix: string, organizationId: string | null | undefined = scope) =>
      withOrganizationScope(`/app/${itemId ?? ""}/subscription/${suffix}`, organizationId),
    [itemId, scope],
  );
};
