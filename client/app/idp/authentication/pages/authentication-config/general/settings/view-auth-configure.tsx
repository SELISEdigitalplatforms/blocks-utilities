import { ReactNode } from "react";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useProjectStore, useT } from "@seliseblocks/genesis-os";

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
  const { t } = useT("utilities.idp.authentication");
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const { data, isLoading, isFetching } = useGetAuthConfig({ projectKey: tenantId });

  if (isLoading || isFetching) return <LoadingSkelton />;

  return (
    <div className="grid grid-cols-1 gap-4 md:grid-cols-2 md:gap-y-8 lg:grid-cols-3">
      <DetailItem label={t("VIEW_AUTH_CONFIGURE.ACCESS_TOKEN_VALIDITY")}>
        {t("VIEW_AUTH_CONFIGURE.MINUTES", { minutes: data?.accessTokenValidForNumberMinutes ?? "" })}</DetailItem>
      <DetailItem label={t("VIEW_AUTH_CONFIGURE.REFRESH_TOKEN_VALIDITY")}>
        {t("VIEW_AUTH_CONFIGURE.MINUTES", { minutes: data?.refreshTokenValidForNumberMinutes ?? "" })}</DetailItem>
      <DetailItem label={t("VIEW_AUTH_CONFIGURE.REMEMBER_ME_TOKEN_VALIDITY")}>
        {t("VIEW_AUTH_CONFIGURE.MINUTES", { minutes: data?.rememberMeRefreshTokenValidForNumberMinutes ?? "" })}</DetailItem>
      <DetailItem label={t("VIEW_AUTH_CONFIGURE.MAX_WRONG_ATTEMPTS_BEFORE_LOCK")}>
        {data?.getNumberOfWrongAttemptsToLockTheAccount}
      </DetailItem>
      <DetailItem label={t("VIEW_AUTH_CONFIGURE.ACCOUNT_LOCK_DURATION")}>
        {t("VIEW_AUTH_CONFIGURE.MINUTES", { minutes: data?.accountLockDurationInMinutes ?? "" })}</DetailItem>
      <DetailItem label={t("VIEW_AUTH_CONFIGURE.PUBLIC_CERTIFICATE")}>
        <UrlWithActions url={data?.publicCertificatePath || ""} />
      </DetailItem>
    </div>
  );
};
