import { useGetMe } from "@/idp/iam/hooks/use-user";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useContext } from "react";
import { profileMfaContext } from "./profile-mfa";
import { useT } from "@seliseblocks/genesis-os";
const LoadingSkelton = () => {
  return (
    <>
      <Skeleton className="h-6 w-1/2" />
      <Skeleton className="my-2 h-6" />
      <Skeleton className="my-2 h-6" />
    </>
  );
};
export const ProfileMFADetails = () => {
  const { t } = useT("utilities.idp");
  const { projectKey, userId } = useContext(profileMfaContext);
  const { isLoading, data } = useGetMe();
  if (isLoading) return <LoadingSkelton />;
  return (
    <>
      {data?.data.mfaEnabled ? (
        <div className="text-base font-normal text-high-emphasis">
          {t("PROFILE_MFA_DETAIL.MULTI_FACTOR_AUTHENTICATION_MFA_IS")}
        </div>
      ) : (
        <div className="text-base font-normal text-high-emphasis">
          {t("PROFILE_MFA_DETAIL.ENABLING_MULTI_FACTOR")}
        </div>
      )}
    </>
  );
};
