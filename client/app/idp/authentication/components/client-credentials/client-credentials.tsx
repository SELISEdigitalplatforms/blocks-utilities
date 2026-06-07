import { useProjectStore } from "@/store/useProjectStore";
// import { GRANT_TYPES } from "@blocks-idp/authentication/constants/authentication.constant";
import { useGetAuthConfig } from "@blocks-idp/authentication/hooks/use-auth-config";
import { ClientCredentialList } from "./client-credentials-list";

export const ClientCredentials = () => {
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const { isLoading } = useGetAuthConfig({ projectKey: tenantId });

  // const isClientCredentialAllowed = authConfig?.allowedGrantTypes?.includes(GRANT_TYPES.clientCredential);

  if (isLoading) {
    return <ClientCredentialList />;
  }

  // if (!isClientCredentialAllowed) {
  //   return (
  //     <div className="w-full min-w-0 space-y-4">
  //       <div className="text-blocks-error flex flex-col items-center justify-center gap-1 rounded-sm border border-base-error bg-blocks-error-100 px-4 py-4 text-base font-normal text-blocks-error-800 md:flex-row">
  //         Please select the &apos;Client Credential&apos; grant type on the <strong>General</strong> tab, then return here to
  //         configure client credentials.
  //       </div>
  //       <div className="flex min-h-[min(50vh,320px)] w-full items-center justify-center rounded-lg border border-dashed bg-muted/20 px-6 py-10 text-center text-sm text-muted-foreground">
  //         Grant type must be enabled before you can add or view client credentials.
  //       </div>
  //     </div>
  //   );
  // }

  return (
    <div className="w-full min-w-0">
      <ClientCredentialList />
    </div>
  );
};
