import type { TFunction } from "@seliseblocks/genesis-os";
import { Link } from "react-router";

export const getMicrosoftStepupDocs = (t: TFunction) => [
  {
      id: "0",
      description: (
        <div>
          <p>
            {t("SSO_SETUP_GUIDELINE_MICROSOFT.THE_MICROSOFT_SOCIAL_CONNECTION")}
          </p>
          <p className="mt-2">
            {t("SSO_SETUP_GUIDELINE_MICROSOFT.AUTH0_AUTOMATICALLY_SYNCS_USER")}
          </p>
        </div>
      ),
    },
    {
      id: "1",
      description: (
        <div>
          <h4 className="text-lg text-high-emphasis">{t("SSO_SETUP_GUIDELINE_MICROSOFT.PREREQUISITES")}</h4>
          <p>{t("SSO_SETUP_GUIDELINE_MICROSOFT.BEFORE_YOU_BEGIN")}</p>
          <ul className="mt-2 list-inside list-disc">
            <li>
              <Link to="">{t("SSO_SETUP_GUIDELINE_MICROSOFT.SIGN_UP_FOR_AN_AZURE")}</Link>
            </li>
            <li>
              <Link to="">{t("SSO_SETUP_GUIDELINE_MICROSOFT.CREATE_AN_AZURE_AD_TENANT")}</Link>
            </li>
          </ul>
        </div>
      ),
    },
    {
      id: "2",
      description: (
        <div>
          <h4 className="text-lg text-high-emphasis">{t("SSO_SETUP_GUIDELINE_MICROSOFT.TEST_CONNECTION")}</h4>
          <p>{t("SSO_SETUP_GUIDELINE_MICROSOFT.YOU_RE_READY_TO_TEST")}</p>
        </div>
      ),
    },
];
