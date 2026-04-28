import { ReactNode } from "react";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useProjectStore } from "@/store/useProjectStore";
import { useGetAuthConfig } from "@blocks-idp/authentication/hooks/use-auth-config";
import { UrlWithActions } from "./url-with-actions";

const DetailItem = ({ label, children }: { label: string; children: ReactNode }) => {
  return (
    <div className="flex flex-col gap-1">
      <p className="text-sm font-medium text-low-emphasis">{label}</p>
      <div className="text-base font-normal text-high-emphasis">{children}</div>
    </div>
  );
};

const LoadingSkelton = () => {
  return (
    <div className="grid grid-cols-1 gap-4 md:grid-cols-3 md:gap-y-8">
      {Array.from({ length: 6 }).map((_item, index) => (
        <div key={index}>
          <Skeleton className="h-5 w-32"></Skeleton>
          <Skeleton className="mt-2 h-6 w-full"></Skeleton>
        </div>
      ))}
    </div>
  );
};

export const ViewAuthConfigure = () => {
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const { data, isLoading, isFetching } = useGetAuthConfig({ projectKey: tenantId });

  if (isLoading || isFetching) return <LoadingSkelton />;

  return (
    <div className="grid grid-cols-1 gap-4 md:grid-cols-2 md:gap-y-8 lg:grid-cols-3">
      <DetailItem label="Access Token Validity">
        {data?.accessTokenValidForNumberMinutes} minutes
      </DetailItem>
      <DetailItem label="Refresh Token Validity">
        {data?.refreshTokenValidForNumberMinutes} minutes
      </DetailItem>
      <DetailItem label="'Remember Me' Token Validity">
        {data?.rememberMeRefreshTokenValidForNumberMinutes} minutes
      </DetailItem>
      <DetailItem label="Max Wrong Attempts Before Lock">
        {data?.getNumberOfWrongAttemptsToLockTheAccount}
      </DetailItem>
      <DetailItem label="Account Lock Duration">
        {data?.accountLockDurationInMinutes} minutes
      </DetailItem>
      <DetailItem label="Public Certificate">
        <UrlWithActions url={data?.publicCertificatePath || ""} />
      </DetailItem>
    </div>
  );
};
