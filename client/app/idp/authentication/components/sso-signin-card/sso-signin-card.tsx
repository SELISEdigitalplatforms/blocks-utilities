import { useCallback, useMemo } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { showErrorToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { ISsoProviderConfigurationWithMeta } from "@blocks-idp/authentication/models/sso.model";
import { oauthService } from "@blocks-idp/authentication/services/oauth.service";
import { useTheme } from "@/hooks/use-theme";
import { sanitizeProviderUrl } from "@blocks-idp/authentication/utils/sanitize-provider-url.util";
import { useT } from "@seliseblocks/genesis-os";

type SSOSigninCardProps = {
  providerConfig: ISsoProviderConfigurationWithMeta;
  withLabel?: boolean;
};

export const SSOSigninCard = ({
  providerConfig,
  withLabel = false,
}: SSOSigninCardProps) => {
  const { t } = useT("utilities.idp.authentication");
  const { theme } = useTheme();

  const onClickHandler = useCallback(async () => {
    try {
      if (!providerConfig.audience || !providerConfig.provider)
        return showErrorToast({ errors: t("SSO_SIGNIN_CARD.SOMETHING_WENT_WRONG") });

      sessionStorage.setItem("clicked_sso_provider", providerConfig.provider);
      sessionStorage.setItem(
        "clicked_sso_audience",
        providerConfig.audience || "",
      );

      const res = await oauthService.getSocialLoginEndpoint({
        provider: providerConfig.provider,
        audience: providerConfig.audience,
        sendAsResponse: true,
      });

      if (res.error) return showErrorToast({ errors: res.error });
      if (!res.providerUrl)
        return showErrorToast({ errors: t("SSO_SIGNIN_CARD.NO_REDIRECT_URL_PROVIDED") });
      window.location.href = sanitizeProviderUrl(res.providerUrl);
    } catch (error) {
      if (isErrorWithErrors(error))
        return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: t("SSO_SIGNIN_CARD.SOMETHING_WENT_WRONG") });
    }
  }, [providerConfig, t]);

  const imageSrc = useMemo(() => {
    return theme === "light"
      ? providerConfig.imageSrc
      : providerConfig.imageSrcDark || providerConfig.imageSrc;
  }, [providerConfig, theme]);

  return (
    <Button variant="outline" className="w-full gap-2" onClick={onClickHandler}>
      <img
        src={imageSrc}
        className="size-5 object-contain"
        alt={providerConfig.provider}
      />
      {withLabel && <>{t("SSO_SIGNIN_CARD.SIGN_IN_WITH", { label: providerConfig.label })}</>}
    </Button>
  );
};
