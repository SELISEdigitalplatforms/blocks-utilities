import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useContext } from "react";
import { userMfaContext } from "./user-mfa";

const LoadingSkelton = () => {
  return (
    <>
      <Skeleton className="h-6 w-1/2" />
      <Skeleton className="my-2 h-6" />
      <Skeleton className="my-2 h-6" />
    </>
  );
};

export const UserMFADetails = () => {
  const { projectKey, userId } = useContext(userMfaContext);
  const { isLoading, data } = useGetUserById({ id: userId, projectKey });

  if (isLoading) return <LoadingSkelton />;

  return (
    <>
      {data?.data.mfaEnabled ? (
        <div className="text-base font-normal text-high-emphasis">
          Multi-Factor Authentication (MFA) is enabled on your account, providing an extra layer of
          security against unauthorized access. Even if your password is compromised, MFA helps keep
          your account safe. By requiring additional verification, it ensures stronger protection
          for your sensitive information
        </div>
      ) : (
        <div className="text-base font-normal text-high-emphasis">
          Multi-Factor Authentication (MFA) is currently disabled for this user
        </div>
      )}
    </>
  );
};
