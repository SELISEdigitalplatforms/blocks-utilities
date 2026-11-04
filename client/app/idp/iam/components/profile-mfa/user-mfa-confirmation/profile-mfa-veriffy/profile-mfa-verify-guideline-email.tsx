import { useContext } from "react";
import { profileMfaContext } from "../../profile-mfa";
import { useGetMe } from "@/idp/iam/hooks/use-user";
const imageUrl = "/assets/images/mail-sent.png";
import { Button } from "@/components/ui-kits/button/button";
import { useResendOtp } from "@/idp/mfa/hooks/use-resend-otp";
import { useT } from "@seliseblocks/genesis-os";
export const ProfileMfaVerifyGuideLineEmail = ({ mfaId }: { mfaId: string }) => {
  const { t } = useT("utilities.idp");
  const { userId, projectKey } = useContext(profileMfaContext);
  const { data } = useGetMe();
  const { remainingTime, resend } = useResendOtp({ mfaId });
  const resendButtonLabel = remainingTime
    ? t("PROFILE_MFA_VERIFY_GUIDELINE_EMAIL.RESEND_IN", { time: `${Math.floor(remainingTime / 60)}:${String(remainingTime % 60).padStart(2, "0")}` })
    : t("PROFILE_MFA_VERIFY_GUIDELINE_EMAIL.RESEND");
  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-center">
        <img src={imageUrl} alt={t("PROFILE_MFA_VERIFY_GUIDELINE_EMAIL.EMAIL_SENT")} width="120" height="100" />
      </div>
      <div className="mt-6">
        <h3 className="text-2xl font-bold">{t("PROFILE_MFA_VERIFY_GUIDELINE_EMAIL.EMAIL_SENT_2")}</h3>
        <p className="mt-2">
          {t("PROFILE_MFA_VERIFY_GUIDELINE_EMAIL.WE_VE_SENT_A_VERIFICATION", { email: (data?.data.email) ?? "" })}
        </p>
      </div>
      <p>
        {t("PROFILE_MFA_VERIFY_GUIDELINE_EMAIL.DID_NOT_RECEIVE_EMAIL")}{" "}
        <Button
          variant="link"
          className="p-0 text-sm font-medium !no-underline"
          disabled={!!remainingTime}
          onClick={resend}
        >
          {resendButtonLabel}
        </Button>
      </p>
      <p>{t("PROFILE_MFA_VERIFY_GUIDELINE_EMAIL.PLEASE_ENTER_THE_KEY_BELOW")} </p>
    </div>
  );
};
