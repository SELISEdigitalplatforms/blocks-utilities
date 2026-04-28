

import { useQueryState } from "nuqs";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";
import { useProjectStore } from "@/store/useProjectStore";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { UserDetails } from "../../../components/user-details";
import { UserActionMenu } from "./user-action-menu";
import { UserDevices } from "../user-devices";
import { UserHistories } from "../user-histories";
import { UserMemberships } from "../user-memberships";
// import { UserRoles } from "../user-roles";
// import { UserPermissions } from "../user-permssions";

const Menu = [
  {
    id: 1,
    label: "Details",
    value: "details",
  },
  {
    id: 3,
    label: "Devices",
    value: "devices",
  },
  {
    id: 4,
    label: "History",
    value: "history",
  },
];

export const User = ({ id }: { id: string }) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [tabId, setTabId] = useQueryState("userDetails", { defaultValue: "details" });
  const { data } = useGetUserById({ id, projectKey: tenantId });

  BREADCRUMB_CUSTOM_TITLES["/services/iam/user-detail"] = "Users";
  BREADCRUMB_CUSTOM_TITLES[`/services/iam/user-detail/${data?.data?.itemId}`] =
    data?.data.lastName ?? null;
  return (
    <div className="px-4 pt-4 md:px-6 md:pt-6">
      <div>
        <div className="hidden md:flex">
          <PageBreadcrumb breadcrumbIndex={3} />
        </div>
        <div className="flex w-full flex-col">
          <div className="flex items-center justify-between text-base text-high-emphasis md:mt-5">
            <h3 className="text-2xl font-bold tracking-tight">
              {data?.data.firstName} {data?.data.lastName}
            </h3>
          </div>

          {/* mobile view */}
          <div className="md:hidden">
            <div className="mb-5 mt-6 flex items-center justify-between rounded text-base">
              <Select value={tabId} onValueChange={setTabId}>
                <SelectTrigger className="w-[180px]">
                  <SelectValue placeholder="Theme" />
                </SelectTrigger>
                <SelectContent>
                  {Menu.map((item) => (
                    <SelectItem key={item.id} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {tabId === "details" ? <UserActionMenu id={id} projectKey={tenantId} /> : null}
            </div>
          </div>

          {/* desktop view */}
          <div className="hidden md:block">
            <Tabs value={tabId} onValueChange={setTabId}>
              <div className="mb-5 mt-6 flex items-center justify-between rounded text-base">
                <TabsList>
                  {Menu.map((item) => (
                    <TabsTrigger key={item.id} value={item.value}>
                      {item.label}
                    </TabsTrigger>
                  ))}
                </TabsList>
                {tabId === "details" ? <UserActionMenu id={id} projectKey={tenantId} /> : null}
              </div>
            </Tabs>
          </div>

          <>
            {tabId === "details" ?
              (
                <div className="flex flex-col gap-6">
                  <UserDetails id={id} />
                  <div className="flex flex-col gap-6">
                    <UserMemberships id={id} projectKey={tenantId} />
                    {/* <UserRoles id={id} projectKey={tenantId} />
                    <UserPermissions userId={id} projectKey={tenantId} /> */}
                  </div>
                </div>
              )
              : null}
            {/* {tabId === "rolesAndPermissions" && (
              <div className="flex flex-col gap-6">
                <UserRoles id={id} projectKey={tenantId} />
                <UserPermissions userId={id} projectKey={tenantId} />
              </div>
            )} */}
            {tabId === "devices" && <UserDevices id={id} projectKey={tenantId} />}
            {tabId === "history" && <UserHistories id={id} projectKey={tenantId} />}
          </>
        </div>
      </div>
    </div>
  );
};
