import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";

import { useCallback, useContext, useEffect, useRef, useState } from "react";
import { UserMfaVerifyForm } from "./user-mfa-verify-form";
import { userMfaContext } from "../../user-mfa";
import { UserMfaVerifyGuideLineTotp } from "./user-mfa-verify-guideline-totp";
import { UserMfaVerifyGuideLineEmail } from "./user-mfa-verify-guideline-email";
import { useGenerateUserMfaOTP } from "@blocks-idp/mfa/hooks/use-mfa-config";

export const UserMFAVerify = () => {
  const { setIsTotpModalOpen, isTotpModalOpen, mfaMethodType, projectKey, userId } =
    useContext(userMfaContext);
  const { mutateAsync } = useGenerateUserMfaOTP();
  const isFirstMount = useRef<boolean>(true);
  const [mfaId, setMfaId] = useState<string>("");

  const generateOtp = useCallback(async () => {
    try {
      const res = await mutateAsync({ projectKey, userId, mfaType: mfaMethodType });
      if (!res.isSuccess) return setIsTotpModalOpen(false);
      setMfaId(res.mfaId);
    } catch (_error) {
      //
    }
  }, [mfaMethodType, mutateAsync, projectKey, setIsTotpModalOpen, userId]);

  useEffect(() => {
    if (isFirstMount.current && isTotpModalOpen) {
      isFirstMount.current = false;
      generateOtp();
    }
  }, [generateOtp, isFirstMount, isTotpModalOpen]);

  return (
    <>
      <Dialog open={isTotpModalOpen} onOpenChange={setIsTotpModalOpen}>
        <DialogContent>
          {mfaMethodType === 1 && (
            <DialogHeader>
              <DialogTitle>Set up your authenticator app</DialogTitle>
              <DialogDescription>Please follow the instructions below.</DialogDescription>
            </DialogHeader>
          )}
          <div className="text-sm font-normal text-high-emphasis">
            {mfaMethodType === 1 ? <UserMfaVerifyGuideLineTotp /> : <UserMfaVerifyGuideLineEmail />}
            <div className="mt-4">
              <UserMfaVerifyForm mfaId={mfaId} />
            </div>
          </div>
        </DialogContent>
      </Dialog>
    </>
  );
};
