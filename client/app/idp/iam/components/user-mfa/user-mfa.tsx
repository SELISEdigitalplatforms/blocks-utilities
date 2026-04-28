import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";
import { UserMFAConfirmationDisable } from "./user-mfa-confirmation/user-mfa-confirmation-disable";
import { UserMFADetails } from "./user-mfa-detail";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useGetMFAConfig } from "@blocks-idp/mfa/hooks/use-mfa-config";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui-kits/button/button";
import { createContext, useContext, useState } from "react";

type UserMFAProps = {
  userId: string;
  projectKey: string;
  enableTotpModal?: boolean;
};

export const UserConfigMFA = () => {
  const { projectKey, userId } = useContext(userMfaContext);
  const { isLoading, isFetching, data } = useGetUserById({ id: userId, projectKey });
  const loading = isFetching || isLoading;
  return (
    <Card>
      <CardHeader>
        <div className="flex items-center justify-between">
          <CardTitle>Multi-factor Authentication</CardTitle>
          {loading ? (
            <Skeleton className="h-6 w-1/6" />
          ) : (
            <>
              {/* {!data?.data?.mfaEnabled && <UserMFAConfirmationEnable />} */}
              {data?.data?.mfaEnabled && <UserMFAConfirmationDisable />}
            </>
          )}
        </div>
      </CardHeader>
      <CardContent>
        <UserMFADetails />
        {/* {data?.data?.mfaEnabled && <UserMFAConfigManage />} */}
      </CardContent>
    </Card>
  );
};

export const ProjectMFA = () => {
  return (
    <Card>
      <CardHeader>
        <div className="flex items-center justify-between">
          <CardTitle>Multi-factor Authentication</CardTitle>
          <Button asChild variant="outline" size="sm">
            <Link to="/services/secret-management?tab=mfa">Go to MFA Settings</Link>
          </Button>
        </div>
      </CardHeader>
      <CardContent className="!pt-0">
        <div className="space-y-2 text-base font-normal text-high-emphasis">
          <p>
            Multi-Factor Authentication (MFA) enhances your account security by requiring an
            additional verification step. To enable MFA, you need to first activate it for your
            project.
          </p>
        </div>
      </CardContent>
    </Card>
  );
};

const LoadingSkelton = () => {
  return (
    <Card className="rounded shadow-none">
      <CardHeader>
        <div className="flex items-center justify-between">
          <CardTitle className="text-xl text-high-emphasis">Multi-factor Authentication</CardTitle>
          <Skeleton className="h-6 w-1/6" />
        </div>
      </CardHeader>
      <CardContent className="!pt-0">
        <Skeleton className="h-6 w-1/2" />
        <Skeleton className="my-2 h-6" />
        <Skeleton className="my-2 h-6" />
      </CardContent>
    </Card>
  );
};

export const userMfaContext = createContext<
  UserMFAProps & {
    isTotpModalOpen: boolean;
    setIsTotpModalOpen: (value: boolean) => void;
    showTotpModal: (type: number) => void;
    mfaMethodType: number;
  }
>({
  projectKey: "",
  userId: "",
  enableTotpModal: false,
  isTotpModalOpen: false,
  setIsTotpModalOpen: () => {},
  showTotpModal: () => {},
  mfaMethodType: 0,
});

export const UserMFA = (props: UserMFAProps) => {
  const { projectKey } = props;
  const [isTotpModalOpen, setIsTotpModalOpen] = useState<boolean>(false);
  const [mfaMethodType, setMfaMethodType] = useState<number>(0);
  const { isLoading, data } = useGetMFAConfig({ projectKey });
  if (isLoading) return <LoadingSkelton />;
  if (!data?.enableMfa) return <ProjectMFA />;

  const showTotpModal = (type: number) => {
    setMfaMethodType(type);
    setIsTotpModalOpen(true);
  };

  return (
    <userMfaContext.Provider
      value={{ ...props, isTotpModalOpen, setIsTotpModalOpen, showTotpModal, mfaMethodType }}
    >
      <UserConfigMFA />
    </userMfaContext.Provider>
  );
};
