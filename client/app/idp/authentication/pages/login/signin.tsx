
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { showErrorToast } from "@/hooks/use-toast";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { GRANT_TYPES } from "@blocks-idp/authentication/constants/authentication.constant";
import { useGetLoginOptions } from "@blocks-idp/authentication/hooks/use-auth";
import { useGetSignUpSetting } from "@blocks-idp/iam/hooks/use-user";
import { Link } from "react-router-dom";
import { useEffect } from "react";
import { SigninForm } from "./signin-form";
import { SsoSignin } from "./sso-signin";

type SigninProps = {
  ssoError?: string;
};

const SigninSkeleton = () => (
  <Card className="flex h-full flex-col rounded border-solid border-background shadow-none md:min-w-[448px] md:border-[#95ADC4] lg:max-w-md">
    <CardHeader className="text-center">
      {/* Title */}
      <Skeleton className="mx-auto h-9 w-40 rounded-md" />
      {/* Subtitle */}
      <Skeleton className="mx-auto mt-2 h-7 w-16 rounded-md" />
    </CardHeader>
    <CardContent className="flex flex-1 flex-col justify-between">
      <div className="flex flex-1 flex-col justify-center gap-4">
        {/* Email label + input */}
        <div className="flex flex-col gap-2">
          <Skeleton className="h-4 w-10 rounded" />
          <Skeleton className="h-10 w-full rounded-md" />
        </div>
        {/* Password label + input */}
        <div className="flex flex-col gap-2">
          <Skeleton className="h-4 w-16 rounded" />
          <Skeleton className="h-10 w-full rounded-md" />
        </div>
        {/* Forgot password */}
        <Skeleton className="ml-auto h-4 w-28 rounded" />
        {/* Login button */}
        <Skeleton className="h-10 w-full rounded-md" />
        {/* OR divider */}
        <div className="flex items-center gap-2">
          <Skeleton className="h-px flex-1" />
          <Skeleton className="h-4 w-6 rounded" />
          <Skeleton className="h-px flex-1" />
        </div>
        {/* SSO buttons */}
        <div className="flex flex-col gap-3">
          <Skeleton className="h-10 w-full rounded-md" />
          <Skeleton className="h-10 w-full rounded-md" />
        </div>
      </div>
      {/* Sign up link */}
      <div className="mt-3 flex items-center justify-center gap-2">
        <Skeleton className="h-4 w-24 rounded" />
        <Skeleton className="h-4 w-14 rounded" />
      </div>
    </CardContent>
  </Card>
);

export const Signin = ({ ssoError }: SigninProps) => {
  const projectKey = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";

  const { data: loginOption, isLoading: isLoginOptionLoading } = useGetLoginOptions();
  const { data: signUpSetting, isLoading: isSignUpSettingLoading } = useGetSignUpSetting({
    projectKey,
  });

  useEffect(() => {
    if (ssoError) {
      showErrorToast({ errors: ssoError });
    }
  }, [ssoError]);

  if (isLoginOptionLoading || isSignUpSettingLoading) {
    return <SigninSkeleton />;
  }

  if (!loginOption || loginOption.allowedGrantTypes?.length < 1) return null;

  const showSignUp =
    signUpSetting?.isEmailPasswordSignUpEnabled || signUpSetting?.isSSoSignUpEnabled;

  return (
    <Card className="flex h-full flex-col rounded border-solid border-background shadow-none md:min-w-[448px] md:border-[#95ADC4] lg:max-w-md">
      <CardHeader className="text-center">
        <CardTitle className="text-3xl">Blocks Cloud</CardTitle>
        <CardDescription className="text-xl text-foreground">Log in</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-1 flex-col justify-between">
        <div className="flex flex-1 flex-col justify-center">
          {loginOption?.allowedGrantTypes.includes(GRANT_TYPES.password) && <SigninForm />}
          {loginOption?.allowedGrantTypes.includes(GRANT_TYPES.password) && (
            <div className="my-2 mt-4 flex items-center">
              <hr className="flex-grow border" />
              <span className="mx-2 text-xs text-low-emphasis">OR</span>
              <hr className="flex-grow border" />
            </div>
          )}
          {loginOption?.allowedGrantTypes.includes(GRANT_TYPES.social) && (
            <SsoSignin loginOption={loginOption} />
          )}
        </div>
        {showSignUp && (
          <div className="flex items-center justify-center">
            <div className="mt-3 flex items-center text-medium-emphasis">
              <p>Not a member?</p>
              <Link to="/signup" className="ml-2 inline-block text-sm text-primary">
                Sign up
              </Link>
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
};
