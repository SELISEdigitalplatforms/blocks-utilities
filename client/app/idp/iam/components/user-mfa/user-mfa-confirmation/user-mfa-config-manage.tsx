import { Button } from "@/components/ui-kits/button/button";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useConfigureUserMFA } from "@blocks-idp/mfa/hooks/use-mfa-config";
import { useContext, useEffect, useState } from "react";
import { UserMFAMethodList } from "./user-mfa-methods-list";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";
import { isErrorWithErrors } from "@/lib/error";
import { userMfaContext } from "../user-mfa";

export const UserMFAConfigManage = () => {
  const { projectKey, userId } = useContext(userMfaContext);
  const [type, setType] = useState(0);
  const { isPending, mutateAsync } = useConfigureUserMFA({ id: userId, projectKey });
  const { data: userData, isLoading, isFetching } = useGetUserById({ id: userId, projectKey });

  useEffect(() => {
    if (userData && userData?.data.userMfaType) {
      setType(userData?.data.userMfaType);
    }
  }, [userData, userData?.data.userMfaType]);

  const onClickHandler = async () => {
    try {
      const res = await mutateAsync({
        mfaEnabled: true,
        projectKey,
        userId,
        userMfaType: type,
      });
      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({ description: "Mfa is configured " });
    } catch (error) {
      if (isErrorWithErrors(error)) showErrorToast({ errors: error.errors });
    }
  };
  return (
    <div className="mt-4 flex flex-col gap-4">
      <UserMFAMethodList selected={type} setSelected={setType} projectKey={projectKey} />
      {userData?.data && type !== userData?.data.userMfaType && (
        <Button
          size="sm"
          onClick={onClickHandler}
          disabled={isPending || type === 0 || isLoading || isFetching}
          className="w-fit"
        >
          Save
        </Button>
      )}
    </div>
  );
};
