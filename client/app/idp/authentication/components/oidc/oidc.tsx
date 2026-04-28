import { useProjectStore } from "@/store/useProjectStore";
import { GRANT_TYPES } from "@blocks-idp/authentication/constants/authentication.constant";
import { useGetAuthConfig } from "@blocks-idp/authentication/hooks/use-auth-config";
import { OidcList } from "./oidc-list";

export const OIDC = () => {
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const { data: authConfig, isLoading } = useGetAuthConfig({ projectKey: tenantId });

  return (
    <div>
      <OidcList />
    </div>
  );
};
