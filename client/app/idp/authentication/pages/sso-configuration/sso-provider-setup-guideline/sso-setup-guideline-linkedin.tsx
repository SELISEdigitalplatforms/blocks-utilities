import type { TFunction } from "@seliseblocks/genesis-os";
import { Link } from "react-router";

export const getLinkedinStepupDocs = (t: TFunction) => [
  {
    id: "0",
    description: (
      <div>
        <p>
          {t("SSO_SETUP_GUIDELINE_LINKEDIN.LINKEDIN_LOGIN_ALLOWS_USERS_TO", { b: t("SSO_SETUP_GUIDELINE_LINKEDIN.CLIENT_ID"), b2: t("SSO_SETUP_GUIDELINE_LINKEDIN.CLIENT_SECRET") })}</p>
      </div>
    ),
  },
  {
    id: "1",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">{t("SSO_SETUP_GUIDELINE_LINKEDIN.PREREQUISITES")}</h4>
        <p>{t("SSO_SETUP_GUIDELINE_LINKEDIN.BEFORE_STARTING")}</p>
        <ul className="mt-2 list-inside list-disc">
          <li>
            <Link to="https://www.linkedin.com/developers/" className="text-primary">
              {t("SSO_SETUP_GUIDELINE_LINKEDIN.SIGN_IN_TO_THE_LINKEDIN")}
            </Link>
          </li>
          <li>{t("SSO_SETUP_GUIDELINE_LINKEDIN.ENSURE_YOU_HAVE_A_VERIFIED")}</li>
        </ul>
      </div>
    ),
  },
  {
    id: "4",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">{t("SSO_SETUP_GUIDELINE_LINKEDIN.OBTAIN_OAUTH_CREDENTIALS")}</h4>
        <p>
          {t("SSO_SETUP_GUIDELINE_LINKEDIN.IN_YOUR_LINKEDIN_APP_DASHBOARD", { b: t("SSO_SETUP_GUIDELINE_LINKEDIN.AUTH") })}</p>
        <ul className="mt-2 list-inside list-disc">
          <li><b>{t("SSO_SETUP_GUIDELINE_LINKEDIN.CLIENT_ID")}</b></li>
          <li><b>{t("SSO_SETUP_GUIDELINE_LINKEDIN.CLIENT_SECRET")}</b></li>
        </ul>
        <p className="mt-2">
          {t("SSO_SETUP_GUIDELINE_LINKEDIN.KEEP_THESE_CREDENTIALS_SECURE_AND")}
        </p>
      </div>
    ),
  },
];
