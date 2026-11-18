
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";

import { OidcSigninForm } from "./oidc-signin-form";
import { useT } from "@seliseblocks/genesis-os";

export const OIDCSignin = () => {
  const { t } = useT("utilities.idp.authentication");
  return (
    <Card className="flex h-full flex-col rounded border-solid border-background shadow-none md:min-w-[448px] md:border-[#95ADC4] lg:max-w-md">
      <CardHeader className="text-center">
        {/* eslint-disable-next-line no-restricted-syntax -- product name */}
        <CardTitle className="text-3xl">Blocks Cloud</CardTitle>
        <CardDescription className="text-xl text-foreground">{t("OIDC_SIGNIN.LOG_IN")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-1 flex-col justify-between">
        <div className="flex flex-1 flex-col justify-center">
          <OidcSigninForm />
        </div>
        <div className="mb-3 flex items-center justify-center"></div>
      </CardContent>
    </Card>
  );
};
