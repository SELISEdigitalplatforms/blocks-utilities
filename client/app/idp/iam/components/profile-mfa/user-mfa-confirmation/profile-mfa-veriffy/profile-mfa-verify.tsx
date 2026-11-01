import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { useCallback, useContext, useEffect, useRef, useState } from "react";
import { ProfileMfaVerifyForm } from "./profile-mfa-verify-form";
import { profileMfaContext } from "../../profile-mfa";
import { ProfileMfaVerifyGuideLineTotp } from "./profile-mfa-verify-guideline-totp";
import { ProfileMfaVerifyGuideLineEmail } from "./profile-mfa-verify-guideline-email";
import { useGenerateUserMfaOTP } from "@/idp/mfa/hooks/use-mfa-config";
import { useT } from "@seliseblocks/genesis-os";
export const ProfileMFAVerify = () => {
  const { t } = useT("utilities.idp");
  const { isVerifyModalOpen, setIsVerifyModalOpen, mfaMethodType, projectKey, userId } =
    useContext(profileMfaContext);
  const { mutateAsync } = useGenerateUserMfaOTP();
  const isFirstMount = useRef<boolean>(true);
  const [mfaId, setMfaId] = useState<string>("");
  const generateOtp = useCallback(async () => {
    try {
      const res = await mutateAsync({ projectKey, userId, mfaType: mfaMethodType });
      if (!res.isSuccess) {
        isFirstMount.current = true;
        setIsVerifyModalOpen(false);
      }
      setMfaId(res.mfaId);
    } catch (_error) {
      //
    }
  }, [mfaMethodType, mutateAsync, projectKey, setIsVerifyModalOpen, userId]);
  useEffect(() => {
    if (isFirstMount.current && isVerifyModalOpen) {
      isFirstMount.current = false;
      generateOtp();
    }
  }, [generateOtp, isFirstMount, isVerifyModalOpen]);
  return (
    <>
      <Dialog
        open={isVerifyModalOpen}
        onOpenChange={(value) => {
          if (!value) isFirstMount.current = true;
          setIsVerifyModalOpen(value);
        }}
      >
        <DialogContent aria-describedby={undefined}>
          <DialogHeader>
            <DialogTitle>{mfaMethodType === 1 && t("PROFILE_MFA_VERIFY.SET_UP_YOUR_AUTHENTICATOR_APP")}</DialogTitle>
            <DialogDescription>
              {mfaMethodType === 1 && t("PROFILE_MFA_VERIFY.PLEASE_FOLLOW_THE_INSTRUCTIONS")}
            </DialogDescription>
          </DialogHeader>
          <div className="text-sm font-normal text-high-emphasis">
            {mfaMethodType === 1 ? (
              <ProfileMfaVerifyGuideLineTotp />
            ) : (
              <ProfileMfaVerifyGuideLineEmail mfaId={mfaId} />
            )}
            <div className="mt-4">
              <ProfileMfaVerifyForm mfaId={mfaId} />
            </div>
          </div>
        </DialogContent>
      </Dialog>
    </>
  );
};
