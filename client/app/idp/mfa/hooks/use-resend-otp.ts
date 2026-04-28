import { useCountDown } from "@/hooks/use-count-down";
import { useResendMfaOTP } from "./use-mfa-config";
import { useCallback } from "react";

type ResendOtpProps = {
  mfaId: string;
};

export const useResendOtp = ({ mfaId }: ResendOtpProps) => {
  const { remainingTime, reset } = useCountDown(300);
  const { mutateAsync } = useResendMfaOTP();

  const resend = useCallback(async () => {
    try {
      await mutateAsync({ mfaId });
      reset();
    } catch (error) {
      console.log(error);
    }
  }, [mfaId, mutateAsync, reset]);

  return { remainingTime, reset, resend };
};
