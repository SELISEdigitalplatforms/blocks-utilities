
import { Button } from "@/components/ui-kits/button/button";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { InputOTP, InputOTPGroup, InputOTPSlot } from "@/components/ui-kits/input-otp/input-otp";
import { showErrorToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useAuthStore } from "@/store/useAuthStore";
import { useVerifyMfa } from "@blocks-idp/authentication/hooks/use-auth";
import { useResendOtp } from "@blocks-idp/mfa/hooks/use-resend-otp";
import { zodResolver } from "@hookform/resolvers/zod";
import { useNavigate } from "react-router-dom";
import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";

import { useForm } from "react-hook-form";
import { z } from "zod";

const CustomInputOTPSlot = ({ index }: { index: number }) => {
  return (
    <InputOTPSlot
      index={index}
      className="h-12 w-[46px] rounded-sm border px-4 py-3.5 first:rounded-l-sm first:border-l last:rounded-r-sm"
    />
  );
};
const getFormSchema = (type: number) =>
  z.object({
    code: z.string().min(type === 2 ? 5 : 6),
  });
export const MfaCheckFrom = () => {
  const navigate = useNavigate();
  const [{ mfa_id, mfa_type }] = useQueryStates({
    mfa_id: parseAsString.withDefault(""),
    mfa_type: parseAsInteger.withDefault(0),
  });
  const { isPending } = useVerifyMfa();
  const { setAuthenticated } = useAuthStore();
  const { remainingTime, resend } = useResendOtp({ mfaId: mfa_id });

  const form = useForm<{ code: string }>({
    resolver: zodResolver(getFormSchema(mfa_type)),
    defaultValues: {
      code: "",
    },
  });

  const submitHandler = async ({ code }: { code: string }) => {
    try {
      setAuthenticated();
      // showSuccessToast({ description: "You've successfully logged in" });
      navigate("/console");
    } catch (error) {
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors.error_description || `Something went wrong` });
      } else {
        showErrorToast({ errors: "Something went wrong" });
      }
    }
  };

  const { isValid } = form.formState;

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(submitHandler)}>
        <FormField
          control={form.control}
          name="code"
          render={({ field }) => (
            <FormItem>
              <FormControl>
                <InputOTP maxLength={mfa_type === 1 ? 6 : 5} {...field}>
                  <InputOTPGroup className="w-full justify-between gap-6">
                    <CustomInputOTPSlot index={0} />
                    <CustomInputOTPSlot index={1} />
                    <CustomInputOTPSlot index={2} />
                    <CustomInputOTPSlot index={3} />
                    <CustomInputOTPSlot index={4} />
                    {mfa_type === 1 && <CustomInputOTPSlot index={5} />}
                  </InputOTPGroup>
                </InputOTP>
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        {mfa_type === 2 && (
          <div className="mt-4 text-right">
            <Button
              type="button"
              variant="link"
              className="p-0 text-sm font-medium !no-underline"
              onClick={resend}
              disabled={!!remainingTime}
            >
              Resend Otp
              {remainingTime > 0 &&
                ` (${Math.floor(remainingTime / 60)}:${String(remainingTime % 60).padStart(2, "0")})`}
            </Button>
          </div>
        )}

        <div className="mt-4">
          <Button className="w-full" disabled={!isValid || isPending}>
            Verify
          </Button>
        </div>
      </form>
    </Form>
  );
};
