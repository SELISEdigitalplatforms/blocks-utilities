

import { useOIDCContext } from "@/layouts/oidc-layout";
import { Button } from "@/components/ui-kits/button/button";
import { buildOIDCNavigationUrl } from "@blocks-idp/authentication/utils/oidc-utils";
import { Check } from "lucide-react";
import { Link } from "react-router";
import { useT } from "@seliseblocks/genesis-os";

type OidcEmailConfirmationProps = {
  email?: string;
};

function OidcEmailConfirmation({ email }: OidcEmailConfirmationProps) {
  const { t } = useT("utilities.idp.authentication");
  const { themeColor } = useOIDCContext();

  return (
    <div className="flex min-h-screen flex-col items-center bg-background">
      <Check className="mb-6" size={40} style={{ color: themeColor }} />
      <div className="mx-auto flex w-11/12 flex-col items-center gap-1 text-center sm:max-w-2xl">
        <h3 className="my-6 text-3xl font-bold tracking-tight">{t("EMAIL_SENT_CONFIRMATION.EMAIL_SENT")}</h3>
        <p className="text-xl text-foreground">
          {t("EMAIL_SENT_CONFIRMATION.AN_EMAIL_HAS_BEEN_SENT", { email: email ?? "" })}</p>
        <p className="my-8 text-xl text-foreground">
          {t("EMAIL_SENT_CONFIRMATION.HAVEN_T_GOT_AN_EMAIL")}
        </p>
        <Link to={buildOIDCNavigationUrl("/oidc/forgot-password")}>
          <Button className="mt-4 text-sm" style={{ backgroundColor: themeColor }}>
            {t("EMAIL_SENT_CONFIRMATION.RESEND_EMAIL")}
          </Button>
        </Link>
      </div>
    </div>
  );
}

export { OidcEmailConfirmation };
