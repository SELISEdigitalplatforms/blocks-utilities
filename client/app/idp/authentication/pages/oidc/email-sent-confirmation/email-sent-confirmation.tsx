

import { useOIDCContext } from "@/layouts/oidc-layout";
import { Button } from "@/components/ui-kits/button/button";
import { buildOIDCNavigationUrl } from "@blocks-idp/authentication/utils/oidc-utils";
import { Check } from "lucide-react";
import { Link } from "react-router-dom";

type OidcEmailConfirmationProps = {
  email?: string;
};

function OidcEmailConfirmation({ email }: OidcEmailConfirmationProps) {
  const { themeColor } = useOIDCContext();

  return (
    <div className="flex min-h-screen flex-col items-center bg-background">
      <Check className="mb-6" size={40} style={{ color: themeColor }} />
      <div className="mx-auto flex w-11/12 flex-col items-center gap-1 text-center sm:max-w-2xl">
        <h3 className="my-6 text-3xl font-bold tracking-tight">Email sent</h3>
        <p className="text-xl text-foreground">
          An email has been sent to {email}. Please, follow the link on the email to continue your
          sign in.
        </p>
        <p className="my-8 text-xl text-foreground">
          Haven&apos;t got an email? Use the button below to resend verification email
        </p>
        <Link to={buildOIDCNavigationUrl("/oidc/forgot-password")}>
          <Button className="mt-4 text-sm" style={{ backgroundColor: themeColor }}>
            Resend email
          </Button>
        </Link>
      </div>
    </div>
  );
}

export { OidcEmailConfirmation };
