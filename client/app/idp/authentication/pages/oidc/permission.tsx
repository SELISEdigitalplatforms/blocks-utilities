
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardDescription, CardHeader } from "@/components/ui-kits/card/card";
import { Link } from "react-router";
import { useState, useEffect, useRef } from "react";
import { useOIDCContext } from "@/layouts/oidc-layout";
import { userAcknowledgement } from "@blocks-idp/authentication/services/oidc-auth-flow.service";
import { useT } from "@seliseblocks/genesis-os";

export const OIDCPermissionScreen = () => {
  const { t } = useT("utilities.idp.authentication");
  const contextValues = useOIDCContext();
  const { userName, themeColor, state, nonce, scope, redirectUri } = contextValues;
  const [isSubmitting, setIsSubmitting] = useState(false);

  const contextRef = useRef(contextValues);

  useEffect(() => {
    contextRef.current = contextValues;
  }, [contextValues, state, scope, redirectUri, nonce]);

  const handleDeny = () => {
    const currentContext = contextRef.current;

    if (!currentContext.redirectUri) {
      console.error("No redirect URI available");
      return;
    }

    const redirectUrl = new URL(currentContext.redirectUri);
    redirectUrl.searchParams.set("error", "access_denied");
    redirectUrl.searchParams.set("error_description", "User denied the authorization request");

    if (currentContext.state) {
      redirectUrl.searchParams.set("state", currentContext.state);
    }

    window.location.href = redirectUrl.toString();
  };

  const handleAllow = async () => {
    const currentContext = contextRef.current;

    if (!currentContext.clientId || !currentContext.projectKey) {
      return;
    }

    setIsSubmitting(true);
    try {
      const result = await userAcknowledgement({
        scope: currentContext.scope || "",
        redirectUri: currentContext.redirectUri || "",
        clientId: currentContext.clientId || "",
        state: currentContext.state || "",
        nonce: currentContext.nonce || "",
        isAcknowledged: true,
        username: currentContext.userName || "",
        projectKey: currentContext.projectKey,
      });

      if (result.redirectUrl) {
        window.location.href = result.redirectUrl;
      } else {
        console.error("No redirect URL in acknowledgement response:", result);
      }
    } catch (error) {
      console.error("Error during acknowledgement:", error);
    } finally {
      setIsSubmitting(false);
    }
  };


  return (
    <Card className="flex h-full flex-col rounded border-solid border-background shadow-none md:min-w-[448px] md:border-[#95ADC4] lg:max-w-md">
      <CardHeader className="text-center">
        <div className="space-y-1">
          <div className="text-3xl font-semibold">{t("PERMISSION.HELLO")}</div>
          {userName && (
            <div className="break-words text-xl font-medium text-muted-foreground">{userName}</div>
          )}
        </div>
        <CardDescription className="mt-3 text-lg text-foreground">
          {t("PERMISSION.YOU_RE_ABOUT_TO_CONNECT")}
        </CardDescription>
      </CardHeader>
      <CardContent className="mt-2 flex flex-1 flex-col justify-between">
        <div className="space-y-4">
          <div className="text-left text-sm text-foreground">
            <p className="font-semibold">{t("PERMISSION.THIS_PORTAL_WOULD_LIKE_TO")}</p>
            <ul className="mt-2 list-inside list-disc space-y-1 pl-4">
              <li>{t("PERMISSION.AUTHENTICATE_YOU_WITH_YOUR_BLOCKS")}</li>
              <li>{t("PERMISSION.ACCESS_YOUR_BASIC_PROFILE")}</li>
              <li>{t("PERMISSION.ACCESS_YOUR_PROJECT_DETAILS")}</li>
            </ul>
          </div>
          <div className="my-4 text-left text-sm text-foreground">
            {t("PERMISSION.BY_CLICKING_ALLOW_YOU_PERMIT")}{" "}
            <Link
              to="https://selisegroup.com/software-development-terms/"
              className="underline"
              style={{ color: themeColor }}
              target="_blank"
            >
              {t("PERMISSION.TERMS_OF_SERVICES")}{" "}
            </Link>
            {t("PERMISSION.AND")}{" "}
            <Link
              to="https://selisegroup.com/privacy-policy/"
              className="underline"
              style={{ color: themeColor }}
              target="_blank"
            >
              {t("PERMISSION.PRIVACY_POLICY")}
            </Link>
          </div>
        </div>
        <div className="mt-2 flex items-center justify-center">
          <div className="mt-4 flex w-full gap-2 text-medium-emphasis">
            <Button
              variant="outline"
              className="flex-1"
              disabled={isSubmitting}
              onClick={handleDeny}
            >
              {t("PERMISSION.DENY")}
            </Button>
            <Button
              className="flex-1"
              disabled={isSubmitting}
              onClick={handleAllow}
              style={{ backgroundColor: themeColor }}
            >
              {t("PERMISSION.ALLOW")}
            </Button>
          </div>
        </div>
      </CardContent>
    </Card>
  );
};
