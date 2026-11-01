import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { ProfileMFADetails } from "./profile-mfa-detail";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Link } from "react-router";
import { Button } from "@/components/ui-kits/button/button";
import { createContext, useState } from "react";
import { ProfileMfaMethodSelectList } from "./user-mfa-confirmation/profile-mfa-methods-select-list";
import { useGetMFAConfig } from "@/idp/mfa/hooks/use-mfa-config";
import { useT } from "@seliseblocks/genesis-os";

const UTILITIES_IDP_MODULE = "utilities.idp";
const PROFILE_MFA_MULTI_FACTOR_AUTHENTICATION = "PROFILE_MFA.MULTI_FACTOR_AUTHENTICATION";
type ProfileMFAProps = {
  userId: string;
  projectKey: string;
};
export const ProfileConfigMFA = () => {
  const { t } = useT(UTILITIES_IDP_MODULE);
  return (
    <Card>
      <CardHeader>
        <CardTitle>{t(PROFILE_MFA_MULTI_FACTOR_AUTHENTICATION)}</CardTitle>
      </CardHeader>
      <CardContent>
        <ProfileMFADetails />
        <div className="mt-6">
          <ProfileMfaMethodSelectList />
        </div>
      </CardContent>
    </Card>
  );
};
export const ProjectMFA = () => {
  const { t } = useT(UTILITIES_IDP_MODULE);
  return (
    <Card>
      <CardHeader>
        <div className="flex items-center justify-between">
          <CardTitle>{t(PROFILE_MFA_MULTI_FACTOR_AUTHENTICATION)}</CardTitle>
          <Button asChild variant="outline" size="sm">
            <Link to="/services/secret-management?tab=mfa">{t("PROFILE_MFA.GO_TO_MFA_SETTINGS")}</Link>
          </Button>
        </div>
      </CardHeader>
      <CardContent className="!pt-0">
        <div className="space-y-2 text-base font-normal text-high-emphasis">
          <p>
            {t("PROFILE_MFA.MULTI_FACTOR_AUTHENTICATION_MFA")}
          </p>
        </div>
      </CardContent>
    </Card>
  );
};
const LoadingSkelton = () => {
  const { t } = useT(UTILITIES_IDP_MODULE);
  return (
    <Card className="rounded shadow-none">
      <CardHeader>
        <div className="flex items-center justify-between">
          <CardTitle className="text-xl text-high-emphasis">{t(PROFILE_MFA_MULTI_FACTOR_AUTHENTICATION)}</CardTitle>
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
export const profileMfaContext = createContext<
  ProfileMFAProps & {
    isVerifyModalOpen: boolean;
    setIsVerifyModalOpen: (value: boolean) => void;
    isDisableModalOpen: boolean;
    setIsDisableModalOpen: (value: boolean) => void;
    showVerifyModal: (type: number) => void;
    mfaMethodType: number;
  }
>({
  projectKey: "",
  userId: "",
  isVerifyModalOpen: false,
  setIsVerifyModalOpen: () => {},
  isDisableModalOpen: false,
  setIsDisableModalOpen: () => {},
  showVerifyModal: () => {},
  mfaMethodType: 0,
});
export const ProfileMFA = (props: ProfileMFAProps) => {
  const { projectKey } = props;
  const [isVerifyModalOpen, setIsVerifyModalOpen] = useState<boolean>(false);
  const [isDisableModalOpen, setIsDisableModalOpen] = useState<boolean>(false);
  const [mfaMethodType, setMfaMethodType] = useState<number>(0);
  const { isLoading, data } = useGetMFAConfig({ projectKey });
  if (isLoading) return <LoadingSkelton />;
  if (!data?.enableMfa) return <ProjectMFA />;
  const showVerifyModal = (type: number) => {
    setMfaMethodType(type);
    setIsVerifyModalOpen(true);
  };
  return (
    <profileMfaContext.Provider
      value={{
        ...props,
        isVerifyModalOpen,
        setIsVerifyModalOpen,
        showVerifyModal,
        mfaMethodType,
        isDisableModalOpen,
        setIsDisableModalOpen,
      }}
    >
      <ProfileConfigMFA />
    </profileMfaContext.Provider>
  );
};
