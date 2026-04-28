import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useContext } from "react";
import { profileMfaContext } from "./profile-mfa";

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
  const { projectKey, userId } = useContext(profileMfaContext);
  const { isLoading, data } = useGetUserById({ id: userId, projectKey });

  if (isLoading) return <LoadingSkelton />;

  return (
    <>
      {data?.data.mfaEnabled ? (
        <div className="text-base font-normal text-high-emphasis">
          Multi-Factor Authentication (MFA) is enabled on your account, providing an extra layer of
          security against unauthorized access. Even if your password is compromised, MFA helps keep
          your account safe.
        </div>
      ) : (
        <div className="text-base font-normal text-high-emphasis">
          Enabling Multi-Factor Authentication (MFA) is a simple yet powerful way to protect your
          online presence. Don’t wait for a security breach to realize the importance of MFA. Enable
          it today and enjoy the peace of mind that comes with knowing your accounts are more
          secure.
        </div>
      )}
    </>
  );
};
