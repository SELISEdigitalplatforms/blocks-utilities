import { useProjectStore } from "@/store/useProjectStore";
import { GRANT_TYPES } from "@blocks-idp/authentication/constants/authentication.constant";
import { useGetAuthConfig } from "@blocks-idp/authentication/hooks/use-auth-config";
import { ClientCredentialList } from "./client-credentials-list";
import { cn } from "@/lib/utils";

export const ClientCredentials = () => {
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const { data: authConfig, isLoading } = useGetAuthConfig({ projectKey: tenantId });

  const isClientCredentialAllowed = authConfig?.allowedGrantTypes?.includes(GRANT_TYPES.clientCredential);

  return (
    <div>
      {!isLoading && !isClientCredentialAllowed && (
        <div className="text-blocks-error mb-4 flex flex-col items-center justify-center gap-1 rounded-sm border border-base-error bg-blocks-error-100 px-4 py-4 text-base font-normal text-blocks-error-800 md:flex-row">
          Please select the &apos;Client Credential&apos; grant type to configure client credentials.
        </div>
      )}

      <div className="relative">
        <ClientCredentialList />
        <div
          className={cn(
            "absolute bottom-0 left-0 right-0 top-0 hidden rounded-sm bg-border opacity-80",
            !isLoading && !isClientCredentialAllowed && "block"
          )}
        ></div>
      </div>
    </div>
  );
};
