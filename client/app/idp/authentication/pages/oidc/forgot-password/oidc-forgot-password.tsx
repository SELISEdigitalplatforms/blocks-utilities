
import { OidcForgotPasswordForm } from "./oidc-forgot-password-form";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";

export const OidcForgotPassword = () => {
  return (
    <Card className="flex h-full flex-col rounded border-solid border-background py-6 shadow-none md:min-w-[448px] md:border-[#95ADC4] md:py-4 lg:max-w-md">
      <CardHeader className="text-center">
        <CardTitle className="text-3xl">Blocks Cloud</CardTitle>
        <CardDescription className="text-xl text-foreground">
          We will email you a password reset link
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-1 flex-col justify-between">
        <div className="flex flex-1 flex-col justify-center">
          <OidcForgotPasswordForm />
        </div>
        <div className="mb-3 flex items-center justify-center"></div>
      </CardContent>
    </Card>
  );
};
