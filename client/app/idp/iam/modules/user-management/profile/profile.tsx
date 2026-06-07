import { useGetMe } from "@/idp/iam/hooks/use-user";
import { useQueryState } from "nuqs";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui-kits/tabs/tabs";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { UpdateUser } from "../update-user/update-user";
import { ProfileDetails } from "@/idp/iam/components/profile-details/profile-details";
import { UserDevices } from "../user-devices/user-devices";
import { UserHistories } from "../user-histories/user-histories";
import { UserPats } from "../user-pat/user-pats";

const x_blocks_key = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";

export const Profile = () => {
  const { isPending, isLoading, data } = useGetMe();
  if (isPending || isLoading || !data?.data) return null;
  return <UserProfile id={data.data.itemId} />;
};

export const UserProfile = ({ id }: { id: string }) => {
  const [tabId, setTabId] = useQueryState("userDetails", {
    defaultValue: "details",
  });
  const { data } = useGetMe();

  return (
    <div className="">
      <div className="flex w-full flex-col px-5 pt-16 md:p-16">
        <div className="flex items-center justify-between text-base text-high-emphasis md:mt-[20px]">
          <h3 className="text-2xl font-semibold">
            {data?.data?.firstName} {data?.data?.lastName}
          </h3>
        </div>
        <Tabs value={tabId}>
          <div className="mb-5 mt-6 flex items-center justify-between rounded text-base">
            <TabsList>
              <TabsTrigger onClick={() => setTabId("details")} value="details">
                Details
              </TabsTrigger>
              <TabsTrigger onClick={() => setTabId("devices")} value="devices">
                Devices
              </TabsTrigger>
              <TabsTrigger onClick={() => setTabId("history")} value="history">
                History
              </TabsTrigger>
              <TabsTrigger
                onClick={() => setTabId("personalAccessTokens")}
                value="personalAccessTokens"
              >
                PATs
              </TabsTrigger>
            </TabsList>
            {tabId === "details" && (
              <UpdateUser id={id} projectKey={x_blocks_key} own />
            )}
          </div>

          <TabsContent value="details">
            <ProfileDetails id={id} />
          </TabsContent>

          <TabsContent value="devices">
            <UserDevices id={id} projectKey={x_blocks_key} />
          </TabsContent>
          <TabsContent value="history">
            <UserHistories id={id} projectKey={x_blocks_key} />
          </TabsContent>
          <TabsContent value="personalAccessTokens">
            <UserPats id={id} />
          </TabsContent>
        </Tabs>
      </div>
    </div>
  );
};
