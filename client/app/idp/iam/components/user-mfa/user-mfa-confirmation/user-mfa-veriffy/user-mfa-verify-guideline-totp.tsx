import { useGetTotp } from "@blocks-idp/mfa/hooks/use-mfa-config";
import { useContext } from "react";
import { userMfaContext } from "../../user-mfa";

export const UserMfaVerifyGuideLineTotp = () => {
  const { userId, projectKey } = useContext(userMfaContext);
  const { data } = useGetTotp({ id: userId, projectKey });
  return (
    <div className="flex flex-col gap-4">
      <div className="flex gap-1">
        <span>1.</span>
        <span>
          Scan the QR code below or enter the setup key to connect your account with an
          authenticator app.
        </span>
      </div>
      <div className="flex flex-col items-center gap-4">
        <div className="relative aspect-square w-40 rounded-sm border p-2">
          <img alt="qr_code" src={data?.qrImageUrl || ""} />
        </div>
        <div className="text-center">
          <p className="text-medium-emphasis">Or enter this code manually in your app:</p>
          <p className="mt-2 font-semibold">{data?.qrCode}</p>
        </div>
      </div>
      <div className="flex gap-1">
        <span>2.</span>
        <span>Verify the pairing was successful by entering the key displayed on the app</span>
      </div>
    </div>
  );
};
