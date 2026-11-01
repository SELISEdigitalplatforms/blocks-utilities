import type { TFunction } from "@seliseblocks/genesis-os";
import { Link } from "react-router";

export const getXStepupDocs = (t: TFunction) => [
  {
    id: "0",
    description: (
      <div>
        <p>
          {t("SSO_SETUP_GUIDELINE_X.TWITTER_NOW_X_LOGIN_ENABLES")}{" "}
          <Link to="https://developer.x.com/en/portal/dashboard" className="text-primary">
            {t("SSO_SETUP_GUIDELINE_X.X_DEVELOPER_PORTAL")}
          </Link>{t("SSO_SETUP_GUIDELINE_X.OBTAIN_YOUR_AND_AND_CONFIGURE", { b: t("SSO_SETUP_GUIDELINE_X.CLIENT_ID"), b2: t("SSO_SETUP_GUIDELINE_X.CLIENT_SECRET") })}</p>
      </div>
    ),
  },
  {
    id: "1",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">{t("SSO_SETUP_GUIDELINE_X.PREREQUISITES")}</h4>
        <p>{t("SSO_SETUP_GUIDELINE_X.BEFORE_YOU_BEGIN")}</p>
        <ul className="mt-2 list-inside list-disc">
          <li>
            {t("SSO_SETUP_GUIDELINE_X.HAVE_A_VERIFIED")}{" "}
            <Link to="https://twitter.com/" className="text-primary">
              {t("SSO_SETUP_GUIDELINE_X.X_TWITTER_ACCOUNT")}
            </Link>
          </li>
          <li>
            {t("SSO_SETUP_GUIDELINE_X.ACCESS_TO_THE")}{" "}
            <Link to="https://developer.x.com/en/portal/dashboard" className="text-primary">
              {t("SSO_SETUP_GUIDELINE_X.X_DEVELOPER_PORTAL")}
            </Link>
          </li>
        </ul>
      </div>
    ),
  },
  {
    id: "4",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">{t("SSO_SETUP_GUIDELINE_X.OBTAIN_CLIENT_CREDENTIALS")}</h4>
        <ul className="mt-2 list-inside list-disc">
          <li><b>{t("SSO_SETUP_GUIDELINE_X.CLIENT_ID")}</b></li>
          <li><b>{t("SSO_SETUP_GUIDELINE_X.CLIENT_SECRET")}</b></li>
        </ul>
      </div>
    ),
  }
];
