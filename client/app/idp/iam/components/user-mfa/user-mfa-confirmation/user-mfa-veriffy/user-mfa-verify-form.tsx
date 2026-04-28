import { Button } from "@/components/ui-kits/button/button";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { InputOTP, InputOTPGroup, InputOTPSlot } from "@/components/ui-kits/input-otp/input-otp";
import { zodResolver } from "@hookform/resolvers/zod";
import { useContext } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { userMfaContext } from "../../user-mfa";
import { useVerifyMfaOTP } from "@blocks-idp/mfa/hooks/use-mfa-config";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";

const CustomInputOTPSlot = ({ index }: { index: number }) => {
  return (
    <InputOTPSlot
      index={index}
      className="h-12 w-[46px] rounded-sm border px-4 py-3.5 first:rounded-l-sm first:border-l last:rounded-r-sm"
    />
  );
};
const FormSchema = z.object({
  code: z.string().min(5),
});

export const UserMfaVerifyForm = ({ mfaId }: { mfaId: string }) => {
  const { projectKey, setIsTotpModalOpen, mfaMethodType, userId } = useContext(userMfaContext);
  const { mutateAsync } = useVerifyMfaOTP({ id: userId, projectKey });

  const form = useForm<z.infer<typeof FormSchema>>({
    resolver: zodResolver(FormSchema),
    defaultValues: {
      code: "",
    },
  });
  const submitHandler = async ({ code }: z.infer<typeof FormSchema>) => {
    try {
      const verifyOtpResponse = await mutateAsync({
        mfaId,
        verificationCode: code,
        authType: mfaMethodType,
        projectKey,
      });
      if (!verifyOtpResponse.isSuccess) return showErrorToast({ errors: verifyOtpResponse.errors });
      setIsTotpModalOpen(false);
      showSuccessToast({ description: "MFA enabled successfully" });
    } catch (_error) {
      //
    }
  };

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(submitHandler)}>
        <FormField
          control={form.control}
          name="code"
          render={({ field }) => (
            <FormItem>
              <FormControl>
                <InputOTP maxLength={mfaMethodType == 1 ? 6 : 5} {...field}>
                  <InputOTPGroup className="w-full justify-center gap-6">
                    <CustomInputOTPSlot index={0} />
                    <CustomInputOTPSlot index={1} />
                    <CustomInputOTPSlot index={2} />
                    <CustomInputOTPSlot index={3} />
                    <CustomInputOTPSlot index={4} />
                    {mfaMethodType === 1 && <CustomInputOTPSlot index={5} />}
                  </InputOTPGroup>
                </InputOTP>
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <div className="mt-6 flex items-center justify-end gap-4">
          <Button variant="outline" type="button" onClick={() => setIsTotpModalOpen(false)}>
            Cancel
          </Button>
          <Button>Verify</Button>
        </div>
      </form>
    </Form>
  );
};
