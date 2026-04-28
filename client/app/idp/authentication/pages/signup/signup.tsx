
import { SignupForm } from "./signup-form";
import { useGetLoginOptions } from "@blocks-idp/authentication/hooks/use-auth";
import { useGetSignUpSetting } from "@blocks-idp/iam/hooks/use-user";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { Loader } from "lucide-react";

export const Signup = () => {
  const projectKey = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";
  
  const { data: loginOption, isLoading: isLoginOptionLoading } = useGetLoginOptions();
  const { data: signUpSetting, isLoading: isSignUpSettingLoading } = useGetSignUpSetting({ projectKey });

  if (isLoginOptionLoading || isSignUpSettingLoading) {
    return (
      <Card className="flex h-full flex-col rounded border-solid border-background shadow-none md:min-w-[448px] md:border-[#95ADC4] lg:max-w-md">
        <CardContent className="flex flex-1 items-center justify-center">
          <Loader className="h-8 w-8 animate-spin" />
        </CardContent>
      </Card>
    );
  }

  if (!loginOption || loginOption.allowedGrantTypes?.length < 1) return null;

  return (
    <SignupForm
      loginOption={loginOption}
      emailSignUpEnabled={signUpSetting?.isEmailPasswordSignUpEnabled || false}
      ssoSignUpEnabled={signUpSetting?.isSSoSignUpEnabled || false}
    />
  );
};
