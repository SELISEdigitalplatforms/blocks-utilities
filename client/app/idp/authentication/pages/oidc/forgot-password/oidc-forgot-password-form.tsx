import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { Link } from "react-router-dom";
import { useForm } from "react-hook-form";
import { forgotPasswordFormSchema, forgotPasswordFormDefaultValue } from "./utils";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { z } from "zod";
import { showErrorToast } from "@/hooks/use-toast";
import { useNavigate } from "react-router-dom";
import { useState } from "react";
import { isErrorWithErrors } from "@/lib/error";
import { useOIDCContext } from "@/layouts/oidc-layout";
import { buildOIDCNavigationUrl } from "@blocks-idp/authentication/utils/oidc-utils";
import { accountRecover } from "@blocks-idp/authentication/services/oidc-auth-flow.service";

export const OidcForgotPasswordForm = () => {
  const { themeColor, projectKey } = useOIDCContext();
  const [isPending, setIsPending] = useState(false);

  const navigate = useNavigate();
  const form = useForm({
    defaultValues: forgotPasswordFormDefaultValue,
    resolver: zodResolver(forgotPasswordFormSchema),
  });

  const { isValid } = form.formState;

  const onSubmitHandler = async (values: z.infer<typeof forgotPasswordFormSchema>) => {
    try {
      if (!projectKey) return;
      setIsPending(true);
      const res = await accountRecover({
        email: values.email,
        projectKey: projectKey,
      });
      if (!res?.isSuccess) {
        return showErrorToast({ errors: res?.error || "Failed to recover account" });
      }
      const baseUrl = buildOIDCNavigationUrl(`/oidc/email-sent-confirmation`);
      const navUrl = `${baseUrl}&email=${encodeURIComponent(values.email)}`;
      navigate(navUrl);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: "Something went wrong" });
    } finally {
      setIsPending(false);
    }
  };

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(onSubmitHandler)}>
        <div className="grid grid-cols-1 gap-4">
          <FormField
            control={form.control}
            name="email"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Email</FormLabel>
                <FormControl>
                  <Input type="email" placeholder="Enter your email" {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />

          <Button
            type="submit"
            className="w-full rounded"
            style={{ backgroundColor: themeColor }}
            disabled={isPending || !isValid}
          >
            Continue
          </Button>
        </div>
        <div className="mt-4 text-center text-base text-foreground">
          Go back to{" "}
          <Link
            to={buildOIDCNavigationUrl("/oidc/login")}
            className="hover:underline"
            style={{ color: themeColor }}
          >
            Log in
          </Link>
        </div>
      </form>
    </Form>
  );
};
