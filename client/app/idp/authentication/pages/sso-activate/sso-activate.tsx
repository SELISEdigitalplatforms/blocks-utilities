import { useState, useMemo } from "react";
import { useNavigate } from "react-router";
import { getApiUrl } from "@/lib/get-api-path";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { showErrorToast } from "@/hooks/use-toast";
import { useAuthStore, useT } from "@seliseblocks/genesis-os";
import { isErrorWithErrors } from "@/lib/error";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Button } from "@/components/ui-kits/button/button";
import {
  getSocialAuthProvidersConfig,
  SSO_PROVIDERS,
} from "@blocks-idp/authentication/constants/sso-providers.constant";
import { oauthService } from "@blocks-idp/authentication/services/oauth.service";
import { sanitizeProviderUrl } from "@blocks-idp/authentication/utils/sanitize-provider-url.util";
import { Link } from "react-router";

const SSO_ACTIVATE_SOMETHING_WENT_WRONG = "SSO_ACTIVATE.SOMETHING_WENT_WRONG";

type SsoActivateProps = {
  oauthParams: { code: string; username: string };
};

export const SsoActivate = ({ oauthParams }: SsoActivateProps) => {
  const { t } = useT("utilities.idp.authentication");
  const { setAuthenticated, setTokens } = useAuthStore();
  const [isChecked, setIsChecked] = useState(false);
  const [isPending, setIsPending] = useState(false);
  const [providerKey] = useState<string | null>(() =>
    sessionStorage.getItem("clicked_sso_provider"),
  );
  const navigate = useNavigate();

  const { config, providerLabel } = useMemo(() => {
    if (!providerKey) return { config: null, providerLabel: "" };

    const conf = getSocialAuthProvidersConfig()[providerKey as SSO_PROVIDERS];
    const label = providerKey.charAt(0).toUpperCase() + providerKey.slice(1);

    return { config: conf, providerLabel: label };
  }, [providerKey, t]);

  const handleUseDifferentAccount = async () => {
    const audience = sessionStorage.getItem("clicked_sso_audience");
    if (!providerKey || !audience) {
      return showErrorToast({ errors: t(SSO_ACTIVATE_SOMETHING_WENT_WRONG) });
    }

    try {
      const res = await oauthService.getSocialLoginEndpoint({
        provider: providerKey as SSO_PROVIDERS,
        audience: audience,
        sendAsResponse: true,
      });

      if (res.error) return showErrorToast({ errors: res.error });
      if (!res.providerUrl)
        return showErrorToast({ errors: t("SSO_ACTIVATE.NO_REDIRECT_URL_PROVIDED") });

      window.location.href = sanitizeProviderUrl(res.providerUrl);
    } catch (error) {
      if (isErrorWithErrors(error))
        return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: t(SSO_ACTIVATE_SOMETHING_WENT_WRONG) });
    }
  };

  const handleActivate = async () => {
    if (!oauthParams?.code) {
      showErrorToast({ errors: t("SSO_ACTIVATE.CODE_IS_MISSING") });
      return;
    }
    setIsPending(true);
    try {
      const key = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY");
      const appUrl = import.meta.env.BLOCKS_APP_URL;
      const isLocalhost = getRuntimeEnv(
        "BLOCKS_LOCALIZATION_BASE_URL",
      )?.includes("localhost");

      const body = new URLSearchParams();
      body.append("code", oauthParams.code);
      body.append("grant_type", "sso_consent");

      const res = await fetch(getApiUrl("idp/v1", "Authentication/Token"), {
        method: "POST",
        headers: {
          "Content-Type": "application/x-www-form-urlencoded",
          "X-Blocks-Key": key!,
          origin: appUrl!,
        },
        body: body,
        credentials: isLocalhost ? "same-origin" : "include",
      });

      const data = await res.json();

      if (res.ok) {
        sessionStorage.removeItem("clicked_sso_provider");
        sessionStorage.removeItem("clicked_sso_audience");

        // For localhost, save tokens for Authorization Bearer
        if (isLocalhost && data.access_token && data.refresh_token) {
          setTokens(data.access_token, data.refresh_token);
        }

        setAuthenticated();
        navigate("/services/language");
      } else {
        showErrorToast({ errors: data.errors || t(SSO_ACTIVATE_SOMETHING_WENT_WRONG) });
      }
    } catch (error) {
      if (JSON.stringify(error).includes("expire")) {
        showErrorToast({
          errors: t("SSO_ACTIVATE.YOUR_SESSION_HAS_EXPIRED_PLEASE"),
        });
      } else if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors });
      } else {
        showErrorToast({ errors: t(SSO_ACTIVATE_SOMETHING_WENT_WRONG) });
      }
    } finally {
      setIsPending(false);
    }
  };

  return (
    <Card className="w-full rounded border-solid border-background shadow-none md:border-[#95ADC4] lg:max-w-md">
      <CardHeader className="text-center">
        <CardTitle className="text-3xl leading-9">Blocks Cloud</CardTitle>
      </CardHeader>
      <CardContent>
        {config && (
          <>
            <div className="flex items-start gap-3 py-4 text-base font-medium">
              <div className="mt-1">
                <img
                  src={config.imageSrc}
                  width={16}
                  height={16}
                  alt={providerLabel}
                />
              </div>
              <span className="text-medium-emphasis">
                {t("SSO_ACTIVATE.SIGNING_IN_WITH_ACCOUNT", { provider: `${providerLabel}`, username: `${oauthParams.username}` })}</span>
            </div>
            <button
              type="button"
              onClick={handleUseDifferentAccount}
              className="mb-8 cursor-pointer text-base font-semibold text-primary hover:underline"
            >
              {t("SSO_ACTIVATE.USE_A_DIFFERENT_ACCOUNT", { providerLabel })}</button>
          </>
        )}

        <div className="mt-2 flex justify-start gap-2 text-sm text-foreground">
          <Checkbox
            id="terms"
            checked={isChecked}
            onCheckedChange={(checked) => setIsChecked(!!checked)}
            className="mt-1 shrink-0"
          />
          <label
            htmlFor="terms"
            className="cursor-pointer text-sm font-medium peer-disabled:cursor-not-allowed peer-disabled:opacity-70"
          >
            {t("SSO_ACTIVATE.I_AGREE_TO_THE")}{" "}
            <Link
              to="https://selisegroup.com/software-development-terms/"
              className="text-primary underline"
              target="_blank"
            >
              {t("SSO_ACTIVATE.TERMS_OF_SERVICES")}{" "}
            </Link>
            {t("SSO_ACTIVATE.AND_ACKNOWLEDGE_THAT_I_HAVE")}{" "}
            <Link
              to="https://selisegroup.com/privacy-policy/"
              className="text-primary underline"
              target="_blank"
            >
              {t("SSO_ACTIVATE.PRIVACY_POLICY")}
            </Link>
          </label>
        </div>
        <Button
          onClick={handleActivate}
          className="mt-6 w-full rounded"
          disabled={isPending || !isChecked}
        >
          {t("common:CONTINUE")}
        </Button>
      </CardContent>
    </Card>
  );
};
