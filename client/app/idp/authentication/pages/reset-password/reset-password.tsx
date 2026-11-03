
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
import { useT } from "@seliseblocks/genesis-os";

type ResetPasswordProps = {
  code?: string;
  lang?: string;
};
export const ResetPassword = ({ code }: ResetPasswordProps) => {
  const { t } = useT("utilities.idp.authentication");
  if (!code) {
    return (
      <div className="flex min-h-screen flex-col items-center bg-background">
        <div className="mb-4 mt-[136px] p-4">
          <Logo src={"/Logo.svg"} width={128} height={54.931} />
        </div>
        <Card className="mx-auto w-full rounded border-solid border-background shadow-none sm:max-w-md sm:border-[#95ADC4]">
          <CardHeader className="text-center">
            <CardTitle className="text-3xl leading-9">{t("RESET_PASSWORD.INVALID_RESET_LINK")}</CardTitle>
            <CardDescription className="text-xl text-foreground">
              {t("RESET_PASSWORD.THE_RESET_CODE_IS_MISSING")}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <Button asChild className="w-full">
              <a href="/forgot-password">{t("RESET_PASSWORD.REQUEST_A_NEW_RESET_LINK")}</a>
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
          <CardTitle className="text-3xl leading-9">{t("RESET_PASSWORD.SET_A_NEW_PASSWORD")}</CardTitle>
          <CardDescription className="text-xl text-foreground">
            {t("RESET_PASSWORD.CHOOSE_PASSWORD_TO_SECURE_ACCOUNT")}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <ResetPasswordForm code={code} />
        </CardContent>
      </Card>
    </div>
  );
};
