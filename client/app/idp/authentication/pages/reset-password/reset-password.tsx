
import { Logo } from "@/components/logo";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { ResetPasswordForm } from "./reset-password-form";

type ResetPasswordProps = {
  code?: string;
  lang?: string;
};
export const ResetPassword = ({ code }: ResetPasswordProps) => {
  if (!code) {
    return (
      <div className="flex min-h-screen flex-col items-center bg-background">
        <div className="mb-4 mt-[136px] p-4">
          <Logo src={"/Logo.svg"} width={128} height={54.931} />
        </div>
        <Card className="mx-auto w-full rounded border-solid border-background shadow-none sm:max-w-md sm:border-[#95ADC4]">
          <CardHeader className="text-center">
            <CardTitle className="text-3xl leading-9">Invalid reset link</CardTitle>
            <CardDescription className="text-xl text-foreground">
              The reset code is missing or invalid. Please request a new reset link.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <Button asChild className="w-full">
              <a href="/forgot-password">Request a new reset link</a>
            </Button>
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col items-center bg-background">
      <div className="mb-4 mt-[136px] p-4">
        <Logo src={"/Logo.svg"} width={128} height={54.931} />
      </div>
      <Card className="mx-auto w-full rounded border-solid border-background shadow-none sm:max-w-md sm:border-[#95ADC4]">
        <CardHeader className="text-center">
          <CardTitle className="text-3xl leading-9">Set a new password</CardTitle>
          <CardDescription className="text-xl text-foreground">
            Choose password to secure account
          </CardDescription>
        </CardHeader>
        <CardContent>
          <ResetPasswordForm code={code} />
        </CardContent>
      </Card>
    </div>
  );
};
