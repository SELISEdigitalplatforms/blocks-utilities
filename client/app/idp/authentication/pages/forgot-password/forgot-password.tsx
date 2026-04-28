
import { Logo } from "@/components/logo";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";
import { ForgotPasswordForm } from "./forgot-password-form";

export const ForgotPassword = () => {
  return (
    <div className="flex min-h-screen flex-col items-center bg-background">
      <div className="mb-4 mt-[136px] p-4">
        <Logo src={"/Logo.svg"} width={128} height={54.931} />
      </div>
      <Card className="mx-auto w-full rounded border-solid border-background shadow-none sm:max-w-md sm:border-[#95ADC4]">
        <CardHeader className="text-center">
          <CardTitle className="text-3xl leading-9">Blocks Cloud</CardTitle>
          <CardDescription className="text-xl text-foreground">Forgot Password</CardDescription>
        </CardHeader>
        <CardContent>
          <ForgotPasswordForm />
        </CardContent>
      </Card>
    </div>
  );
};
