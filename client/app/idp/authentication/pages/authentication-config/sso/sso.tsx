import { useProjectStore } from "@seliseblocks/genesis-os";

import { SSOProviderList } from "./sso-provider-list";

export const SSO = () => {
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };

  return (
    <div>
      <SSOProviderList />
    </div>
  );
};
