"use client";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { TabsContent } from "@radix-ui/react-tabs";
import { useQueryState } from "nuqs";
// import { GrantTypes } from "./general/grant-types";
// import { SelfSignup } from "./general/self-signup";
// import { GeneralSettings } from "./general/settings";
import { SSO } from "./sso";
import { Certificates } from "./general/certificates/certificates";
import { AuthenticationTabs, GRANT_TYPES } from "@blocks-idp/authentication/constants/authentication.constant";
import { OIDC } from "@blocks-idp/authentication/components/oidc";
import { ClientCredentials } from "@blocks-idp/authentication/components/client-credentials";
import { CreateClientCredential } from "@blocks-idp/authentication/components/create-client-credential";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui-kits/select/select";
import { CreateOIDC } from "@blocks-idp/authentication/components/create-oidc";
import { useProjectStore } from "@/store/useProjectStore";

export const AuthenticationConfig = () => {
  const [selectedTab, setSelectedTab] = useQueryState("tab", { defaultValue: GRANT_TYPES.social });
  const tenantId = useProjectStore().selectedProject?.tenantId || "";

  return (
    <div>
      <div className="mb-[18px] flex items-center justify-between md:mb-[24px]">
        <h1 className="text-lg font-semibold md:text-2xl">IDP</h1>
      </div>
      <Tabs value={selectedTab} onValueChange={(value) => setSelectedTab(value)}>
        <div className="mb-4 flex items-start justify-between gap-4">
          <>
            <TabsList className="hidden w-auto sm:inline-flex">
              {AuthenticationTabs.map((item) => (
                <TabsTrigger key={item.id} value={item.value}>
                  {item.label}
                </TabsTrigger>
              ))}
            </TabsList>
            <div className="sm:hidden">
              <Select value={selectedTab} onValueChange={(value) => setSelectedTab(value)}>
                <SelectTrigger className="w-32 gap-2">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {AuthenticationTabs.map((item) => (
                    <SelectItem key={item.id} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </>

          <>
            {selectedTab === GRANT_TYPES.clientCredential && <CreateClientCredential />}
            {selectedTab === GRANT_TYPES.authorizationCode && <CreateOIDC />}
          </>
        </div>
        {/* <TabsContent value="general" className="grid grid-cols-1 gap-6">
          <GeneralSettings />
          <GrantTypes />
        </TabsContent> */}
        <TabsContent value={GRANT_TYPES.social}>
          <SSO />
        </TabsContent>
        <TabsContent value="external-idp">
          <Certificates />
        </TabsContent>
        <TabsContent value={GRANT_TYPES.clientCredential}>
          <ClientCredentials />
        </TabsContent>
        <TabsContent value={GRANT_TYPES.authorizationCode}>
          <OIDC />
        </TabsContent>
      </Tabs>
    </div>
  );
};
