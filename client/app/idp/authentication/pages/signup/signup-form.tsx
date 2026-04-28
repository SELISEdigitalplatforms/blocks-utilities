
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
import { Link } from "react-router-dom";
import { useNavigate } from "react-router-dom";
import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { SsoSignin } from "../login/sso-signin";
import { signupFormDefaultValue, signupFormSchema } from "./utils";

export const SignupForm = ({
  loginOption,
  emailSignUpEnabled,
  ssoSignUpEnabled,
}: {
  loginOption: LoginOption;
  emailSignUpEnabled: boolean;
  ssoSignUpEnabled: boolean;
}) => {
  const [isChecked, setIsChecked] = useState(false);
  const navigate = useNavigate();
  const form = useForm({
    defaultValues: signupFormDefaultValue,
    resolver: zodResolver(signupFormSchema),
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

  const onSubmitHandler = async (values: z.infer<typeof signupFormSchema>) => {
    try {
      const res = await mutateAsync({
        ...values,
        captchaCode,
      });
      if (!res.isSuccess) {
        resetCaptcha();
        return showErrorToast({ errors: res.errors });
      }
      navigate(`/signup-email-sent?email=${values.email}`);
    } catch (error) {
      resetCaptcha();
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors });
      } else {
        showErrorToast({ errors: "Something went wrong" });
      }
    }
  };

  useEffect(() => {
    if (!isValid && captchaCode) resetCaptcha();
  }, [captchaCode, isValid, resetCaptcha]);

  return (
    <Card className="w-full rounded border-solid border-background shadow-none md:border-[#95ADC4] lg:max-w-md">
      <CardHeader className="text-center">
        <CardTitle className="text-3xl leading-9">Blocks Cloud</CardTitle>
        <CardDescription className="text-xl text-foreground">Sign Up</CardDescription>
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
                      <FormLabel>Email</FormLabel>
                      <FormControl>
                        <Input type="email" placeholder="Enter your email" {...field} />
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
                    I agree to the{" "}
                    <Link
                      to="https://selisegroup.com/software-development-terms/"
                      className="text-primary underline"
                      target="_blank"
                    >
                      Terms of Services{" "}
                    </Link>
                    and acknowledge that I have read the{" "}
                    <Link
                      to="https://selisegroup.com/privacy-policy/"
                      className="text-primary underline"
                      target="_blank"
                    >
                      Privacy policy.
                    </Link>
                  </label>
                </div>
                <Button
                  type="submit"
                  className="w-full rounded"
                  disabled={isPending || !isValid || !captchaCode || !isChecked}
                >
                  Continue
                </Button>
              </div>
            )}
          </form>
        </Form>
        {ssoSignUpEnabled && emailSignUpEnabled && (
          <div className="my-2 flex items-center">
            <hr className="flex-grow border-gray-300" />
            <span className="mx-2 text-xs text-gray-500">OR</span>
            <hr className="flex-grow border-gray-300" />
          </div>
        )}

        {ssoSignUpEnabled && loginOption?.allowedGrantTypes.includes(GRANT_TYPES.social) && (
          <SsoSignin loginOption={loginOption} />
        )}

        <div className="mt-4 text-center text-base text-foreground">
          Already a member?{" "}
          <Link to={"/login"} className="text-primary hover:underline">
            Log in
          </Link>
        </div>
      </CardContent>
    </Card>
  );
};
