import { useContext } from "react";
import { profileMfaContext } from "../../profile-mfa";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";

const imageUrl = "/assets/images/mail-sent.png";
import { Button } from "@/components/ui-kits/button/button";
import { useResendOtp } from "@blocks-idp/mfa/hooks/use-resend-otp";

export const ProfileMfaVerifyGuideLineEmail = ({ mfaId }: { mfaId: string }) => {
  const { userId, projectKey } = useContext(profileMfaContext);
  const { data } = useGetUserById({ id: userId, projectKey });
  const { remainingTime, resend } = useResendOtp({ mfaId });

  const resendButtonLabel = remainingTime
    ? `Resend in (${Math.floor(remainingTime / 60)}:${String(remainingTime % 60).padStart(2, "0")})`
    : "Resend";

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-center">
        <img src={imageUrl} alt="email sent" width="120" height="100" />
      </div>
      <div className="mt-6">
        <h3 className="text-2xl font-bold">Email sent</h3>
        <p className="mt-2">
          We’ve sent a verification key to your registered email address ({data?.data.email}).{" "}
        </p>
      </div>
      <p>
        Did not receive email?{" "}
        <Button
          variant="link"
          className="p-0 text-sm font-medium !no-underline"
          disabled={!!remainingTime}
          onClick={resend}
        >
          {resendButtonLabel}
        </Button>
      </p>
      <p>Please enter the key below to complete your setup. </p>
    </div>
  );
};
