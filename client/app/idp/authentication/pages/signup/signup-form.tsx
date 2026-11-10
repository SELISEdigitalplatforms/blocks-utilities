
import { Captcha } from "@/components/captcha";
import { Button } from "@/components/ui-kits/button/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Input } from "@/components/ui-kits/input/input";
import { showErrorToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { GRANT_TYPES } from "@blocks-idp/authentication/constants/authentication.constant";
import { useSignupByEmail } from "@blocks-idp/authentication/hooks/use-auth";
import { LoginOption } from "@blocks-idp/authentication/models/auth-configuration.model";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { zodResolver } from "@hookform/resolvers/zod";
import { Link } from "react-router";
import { useNavigate } from "react-router";
import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { SsoSignin } from "../login/sso-signin";
import { signupFormDefaultValue, createSignupFormSchema } from "./utils";
import { useT } from "@seliseblocks/genesis-os";

export const SignupForm = ({
  loginOption,
  emailSignUpEnabled,
  ssoSignUpEnabled,
}: {
  loginOption: LoginOption;
  emailSignUpEnabled: boolean;
  ssoSignUpEnabled: boolean;
}) => {
  const { t } = useT("utilities.idp.authentication");
  const [isChecked, setIsChecked] = useState(false);
  const navigate = useNavigate();
  const form = useForm({
    defaultValues: signupFormDefaultValue,
    resolver: zodResolver(createSignupFormSchema()),
  });
  const { isPending, mutateAsync } = useSignupByEmail();

  const googleSiteKey = getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";
  const {
    code: captchaCode,
    captcha,
    reset: resetCaptcha,
  } = useCaptcha({
    type: "reCaptcha-v2-checkbox",
    siteKey: googleSiteKey,
  });

  const { isValid } = form.formState;

  const onSubmitHandler = async (values: z.infer<ReturnType<typeof createSignupFormSchema>>) => {
    try {
      const res = await mutateAsync({
        ...values,
        captchaCode,
      });
      if (!res.isSuccess) {
        resetCaptcha();
        showErrorToast({ errors: res.errors });
        return;
      }
      navigate(`/signup-email-sent?email=${values.email}`);
    } catch (error) {
      resetCaptcha();
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors });
      } else {
        showErrorToast({ errors: t("SIGNUP_FORM.SOMETHING_WENT_WRONG") });
      }
    }
  };

  useEffect(() => {
    if (!isValid && captchaCode) resetCaptcha();
  }, [captchaCode, isValid, resetCaptcha]);

  return (
    <Card className="w-full rounded border-solid border-background shadow-none md:border-[#95ADC4] lg:max-w-md">
      <CardHeader className="text-center">
        {/* eslint-disable-next-line no-restricted-syntax -- product name */}
        <CardTitle className="text-3xl leading-9">Blocks Cloud</CardTitle>
        <CardDescription className="text-xl text-foreground">{t("SIGNUP_FORM.SIGN_UP")}</CardDescription>
      </CardHeader>
      <CardContent>
        <Form {...form}>
          <form onSubmit={form.handleSubmit(onSubmitHandler)}>
            {emailSignUpEnabled && (
              <div className="grid gap-4">
                <FormField
                  control={form.control}
                  name="email"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>{t("common:EMAIL")}</FormLabel>
                      <FormControl>
                        <Input type="email" placeholder={t("SIGNUP_FORM.ENTER_YOUR_EMAIL")} {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                {isValid && <Captcha {...captcha} />}

                <div className="mt-2 flex justify-start gap-2 text-sm text-foreground">
                  <Checkbox
                    id="terms"
                    checked={isChecked}
                    onCheckedChange={(checked) => setIsChecked(!!checked)}
                    className="mt-1 shrink-0"
                  />
                  <label
                    htmlFor="terms"
                    className="cursor-pointer text-sm font-medium peer-disabled:cursor-not-allowed peer-disabled:opacity-70"
                  >
                    {t("SIGNUP_FORM.I_AGREE_TO_THE")}{" "}
                    <Link
                      to="https://selisegroup.com/software-development-terms/"
                      className="text-primary underline"
                      target="_blank"
                    >
                      {t("SIGNUP_FORM.TERMS_OF_SERVICES")}{" "}
                    </Link>
                    {t("SIGNUP_FORM.AND_ACKNOWLEDGE_THAT_I_HAVE")}{" "}
                    <Link
                      to="https://selisegroup.com/privacy-policy/"
                      className="text-primary underline"
                      target="_blank"
                    >
                      {t("SIGNUP_FORM.PRIVACY_POLICY")}
                    </Link>
                  </label>
                </div>
                <Button
                  type="submit"
                  className="w-full rounded"
                  disabled={isPending || !isValid || !captchaCode || !isChecked}
                >
                  {t("common:CONTINUE")}
                </Button>
              </div>
            )}
          </form>
        </Form>
        {ssoSignUpEnabled && emailSignUpEnabled && (
          <div className="my-2 flex items-center">
            <hr className="flex-grow border-gray-300" />
            <span className="mx-2 text-xs text-gray-500">{t("SIGNUP_FORM.OR")}</span>
            <hr className="flex-grow border-gray-300" />
          </div>
        )}

        {ssoSignUpEnabled && loginOption?.allowedGrantTypes.includes(GRANT_TYPES.social) && (
          <SsoSignin loginOption={loginOption} />
        )}

        <div className="mt-4 text-center text-base text-foreground">
          {t("SIGNUP_FORM.ALREADY_A_MEMBER")}{" "}
          <Link to={"/login"} className="text-primary hover:underline">
            {t("SIGNUP_FORM.LOG_IN")}
          </Link>
        </div>
      </CardContent>
    </Card>
  );
};
