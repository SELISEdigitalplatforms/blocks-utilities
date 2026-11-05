import type { TFunction } from "@seliseblocks/genesis-os";
import { Link } from "react-router";

export const getGithubStepupDocs = (t: TFunction) => [
  {
    id: "0",
    description: (
      <div>
        <p>
          {t("SSO_SETUP_GUIDELINE_GITHUB.GITHUB_LOGIN_ALLOWS_USERS_TO")}
        </p>
      </div>
    ),
  },
  {
    id: "1",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">{t("SSO_SETUP_GUIDELINE_GITHUB.PREREQUISITES")}</h4>
        <p>{t("SSO_SETUP_GUIDELINE_GITHUB.BEFORE_YOU_BEGIN")}</p>
        <ul className="mt-2 list-inside list-disc">
          <li>
            <Link to="https://github.com/login" className="text-primary">
              {t("SSO_SETUP_GUIDELINE_GITHUB.SIGN_IN_TO_YOUR_GITHUB")}
            </Link>
          </li>
          <li>
            {t("SSO_SETUP_GUIDELINE_GITHUB.GO_TO")}{" "}
            <Link
              to="https://github.com/settings/developers"
              className="text-primary"
            // eslint-disable-next-line no-restricted-syntax -- product name
            >
              GitHub Developer Settings
            </Link>
          </li>
          <li>{t("SSO_SETUP_GUIDELINE_GITHUB.ENSURE_YOU_HAVE_ADMINISTRATIVE")}</li>
        </ul>
      </div>
    ),
  },
  {
    id: "3",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">{t("SSO_SETUP_GUIDELINE_GITHUB.OBTAIN_CLIENT_CREDENTIALS")}</h4>
        <p>
          {t("SSO_SETUP_GUIDELINE_GITHUB.AFTER_REGISTERING_THE_APP_GITHUB", { b: t("SSO_SETUP_GUIDELINE_GITHUB.CLIENT_ID"), b2: t("SSO_SETUP_GUIDELINE_GITHUB.CLIENT_SECRET") })}</p>
      </div>
    ),
  },
];
