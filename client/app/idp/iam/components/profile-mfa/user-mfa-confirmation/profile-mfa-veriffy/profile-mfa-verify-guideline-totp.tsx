import { useGetTotp } from "@/idp/mfa/hooks/use-mfa-config";
import { useContext } from "react";
import { profileMfaContext } from "../../profile-mfa";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useT } from "@seliseblocks/genesis-os";
export const ProfileMfaVerifyGuideLineTotp = () => {
  const { t } = useT("utilities.idp");
  const { userId, projectKey } = useContext(profileMfaContext);
  const { data } = useGetTotp({ id: userId, projectKey });
  return (
    <div className="flex flex-col gap-4">
      <div className="flex gap-1">
        <span>1.</span>
        <span>
          {t("PROFILE_MFA_VERIFY_GUIDELINE_TOTP.SCAN_THE_QR_CODE_BELOW")}
        </span>
      </div>
      <div className="flex flex-col items-center gap-4">
        <div className="relative aspect-square w-40 rounded-sm border p-2">
          {data?.qrImageUrl ? (
            <img alt={t("PROFILE_MFA_VERIFY_GUIDELINE_TOTP.QR_CODE_ALT")} src={data?.qrImageUrl} />
          ) : (
            <Skeleton />
          )}
        </div>
        <div className="text-center">
          <p className="text-medium-emphasis">{t("PROFILE_MFA_VERIFY_GUIDELINE_TOTP.OR_ENTER_THIS_CODE_MANUALLY")}</p>
          <p className="mt-2 font-semibold">{data?.qrCode}</p>
        </div>
      </div>
      <div className="flex gap-1">
        <span>2.</span>
        <span>{t("PROFILE_MFA_VERIFY_GUIDELINE_TOTP.VERIFY_THE_PAIRING_WAS_SUCCESSFUL")}</span>
      </div>
    </div>
  );
};
