
import { getApiUrl } from "@/lib/get-api-path";
import { ConfigureButton } from "@/components/action-buttons/configure-button";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { clearQueryString } from "@/lib/utils";
import { LogMenu } from "@blocks-lmt/components";
import { InviteUser } from "@blocks-idp/iam/modules/user-management/invite-user/invite-user";
import { Users } from "@blocks-idp/iam/modules/user-management/users";
import { SignupSettings } from "@blocks-idp/iam/modules/user-management/signup-settings";
import { Link } from "react-router-dom";
import { useQueryState } from "nuqs";
import { Button } from "@/components/ui-kits/button/button";

export const IamManagement = () => {
  const [tabId, setTabId] = useQueryState("tab", { defaultValue: "users" });

  return (
    <main className="flex flex-col">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <h3 className="text-2xl font-bold tracking-tight">Identity and Access Management</h3>
        </div>
      
      </div>

      <Tabs
        defaultValue={tabId}
        onValueChange={(value: string) => {
          clearQueryString();
          setTabId(value);
        }}
      >
        <div className="mb-5 mt-6 flex items-center justify-between rounded text-base">
          <TabsList>
            <TabsTrigger value="users">Users</TabsTrigger>
          </TabsList>
          <div className="flex items-center gap-2">
            <SignupSettings />
            <InviteUser />
          </div>
        </div>
        <TabsContent value="users">
          <Users />
        </TabsContent>
      </Tabs>
    </main>
  );
};
