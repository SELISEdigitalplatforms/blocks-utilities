

import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { useGetOrganizationById } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@/store/useProjectStore";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  OrganizationUsers,
  InviteOrganizationUser,
} from "@blocks-idp/iam/modules/organization-management/organization-users";

export const OrganizationDetail = ({ id }: { id: string }) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data, isLoading } = useGetOrganizationById({ itemId: id, projectKey: tenantId });

  BREADCRUMB_CUSTOM_TITLES["/services/iam/organization-detail"] = "Organizations";
  BREADCRUMB_CUSTOM_TITLES[`/services/iam/organization-detail/${id}`] =
    data?.organization?.name ?? null;

  return (
    <main className="flex flex-col">
      <div className="hidden md:flex">
        <PageBreadcrumb breadcrumbIndex={3} />
      </div>
      <div className="flex w-full flex-col">
        <div className="flex items-center justify-between text-base text-high-emphasis md:mt-5">
          {isLoading ? (
            <Skeleton className="h-8 w-48" />
          ) : (
            <h3 className="text-2xl font-bold tracking-tight">
              {data?.organization?.name}
            </h3>
          )}
          <InviteOrganizationUser organizationId={id} />
        </div>
        <div className="mt-6">
          <OrganizationUsers organizationId={id} />
        </div>
      </div>
    </main>
  );
};
