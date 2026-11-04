
import { useOIDCContext } from "@/layouts/oidc-layout";
import { Button } from "@/components/ui-kits/button/button";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";
import { buildOIDCNavigationUrl } from "@blocks-idp/authentication/utils/oidc-utils";
import { AlertCircle } from "lucide-react";
import { Link } from "react-router";
import { useSearchParams } from "react-router";
import { useT } from "@seliseblocks/genesis-os";

const formatErrorCode = (code: string): string =>
  code
    .replace(/_/g, " ")
    .replace(/\b\w/g, (c) => c.toUpperCase());

export const OIDCErrorScreen = () => {
  const { t } = useT("utilities.idp.authentication");
  const { themeColor } = useOIDCContext();
  const [searchParams] = useSearchParams();

  const errorCode = searchParams.get("error");
  const errorDescription = searchParams.get("error_description");

  const hasApiError = !!errorCode || !!errorDescription;

  return (
    <Card className="flex h-full flex-col rounded border-solid border-background shadow-none md:min-w-[448px] md:border-[#95ADC4] lg:max-w-md">
      <CardHeader className="flex flex-col items-center gap-3 pb-2 text-center">
        <div className="flex h-14 w-14 items-center justify-center rounded-full bg-red-50">
          <AlertCircle className="h-7 w-7 text-red-500" />
        </div>
        <CardTitle className="text-2xl font-semibold text-foreground">
          {hasApiError ? t("ERROR_SCREEN.SIGN_IN_FAILED") : t("ERROR_SCREEN.ACCESS_BLOCKED")}
        </CardTitle>
      </CardHeader>
      <CardContent className="mt-2 flex flex-1 flex-col justify-between gap-6">
        <div className="space-y-4">
          {hasApiError ? (
            <div className="space-y-2 rounded-lg border border-red-100 bg-red-50 p-4">
              {errorDescription && (
                <p className="text-sm font-medium text-red-700">{errorDescription}</p>
              )}
              {errorCode && (
                <p className="text-xs text-red-500">
                  {t("ERROR_SCREEN.ERROR_CODE", { span: `${formatErrorCode(errorCode)}` })}
                </p>
              )}
            </div>
          ) : (
            <div className="space-y-2 rounded-lg border border-red-100 bg-red-50 p-4">
              <p className="text-sm font-medium text-red-700">
                {t("ERROR_SCREEN.WE_COULDN_T_SIGN_YOU")}
              </p>
              <p className="text-xs text-red-500">
                {t("ERROR_SCREEN.THE_REQUEST_COULD_NOT_BE")}
              </p>
            </div>
          )}
          <p className="text-center text-sm text-muted-foreground">
            {t("ERROR_SCREEN.IF_THIS_PROBLEM_PERSISTS_PLEASE")}
          </p>
        </div>
        <Link to={buildOIDCNavigationUrl("/oidc/login")} className="w-full">
          <Button
            variant="outline"
            className="w-full rounded font-medium hover:opacity-90"
            style={{ color: themeColor, borderColor: themeColor }}
          >
            {t("ERROR_SCREEN.BACK_TO_SIGN_IN")}
          </Button>
        </Link>
      </CardContent>
    </Card>
  );
};
