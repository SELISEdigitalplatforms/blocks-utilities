import { useProjectStore } from "@seliseblocks/genesis-os";

import { useGetAuthConfig } from "@blocks-idp/authentication/hooks/use-auth-config";
import { OidcList } from "./oidc-list";

export const OIDC = () => {
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  useGetAuthConfig({ projectKey: tenantId });

  return (
    <div>
      <OidcList />
    </div>
  );
};
