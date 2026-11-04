import type { TFunction } from "@seliseblocks/genesis-os";
import { Link } from "react-router";

export const getGoogleStepupDocs = (t: TFunction) => [
  {
    id: "0",
    description: (
      <div>
        <p>
          {t("SSO_SETUP_GUIDELINE_GOOGLE.GOOGLE_LOGIN_ALLOWS_USERS_TO")}
        </p>
      </div>
    ),
  },
  {
    id: "1",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">{t("SSO_SETUP_GUIDELINE_GOOGLE.PREREQUISITES")}</h4>
        <p>{t("SSO_SETUP_GUIDELINE_GOOGLE.BEFORE_YOU_BEGIN")}</p>
        <ul className="mt-2 list-inside list-disc">
          <li>
            <Link to="https://console.cloud.google.com/" className="text-primary">
              {t("SSO_SETUP_GUIDELINE_GOOGLE.SIGN_IN_TO_GOOGLE_CLOUD")}
            </Link>
          </li>
          <li>{t("SSO_SETUP_GUIDELINE_GOOGLE.CREATE_A_NEW_PROJECT_OR")}</li>
        </ul>
      </div>
    ),
  },
  {
    id: "2",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">{t("SSO_SETUP_GUIDELINE_GOOGLE.CONFIGURE_CONSENT_SCREEN")}</h4>
        <p>{t("SSO_SETUP_GUIDELINE_GOOGLE.IN_GOOGLE_CLOUD_CONSOLE")}</p>
        <ul className="mt-2 list-inside list-disc">
          <li>
            {t("SSO_SETUP_GUIDELINE_GOOGLE.GO_TO")}{" "}
            <Link
              to="https://console.cloud.google.com/apis/credentials/consent"
              className="text-primary"
            >
              {t("SSO_SETUP_GUIDELINE_GOOGLE.OAUTH_CONSENT_SCREEN")}
            </Link>
          </li>
          <li>{t("SSO_SETUP_GUIDELINE_GOOGLE.SELECT_EXTERNAL_AS_USER_TYPE")}</li>
          <li>{t("SSO_SETUP_GUIDELINE_GOOGLE.FILL_REQUIRED_FIELDS_LIKE_APP")}</li>
          <li>{t("SSO_SETUP_GUIDELINE_GOOGLE.SAVE_AND_CONTINUE_THROUGH_THE")}</li>
        </ul>
      </div>
    ),
  },
  {
    id: "3",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">{t("SSO_SETUP_GUIDELINE_GOOGLE.CREATE_OAUTH_2_0_CREDENTIALS")}</h4>
        <ul className="mt-2 list-inside list-disc">
          <li>
            {t("SSO_SETUP_GUIDELINE_GOOGLE.GO_TO")}{" "}
            <Link to="https://console.cloud.google.com/apis/credentials" className="text-primary">
              {t("SSO_SETUP_GUIDELINE_GOOGLE.APIS_SERVICES_CREDENTIALS")}
            </Link>
          </li>
          <li>{t("SSO_SETUP_GUIDELINE_GOOGLE.CLICK_CREATE_CREDENTIALS_OAUTH")}</li>
          <li>{t("SSO_SETUP_GUIDELINE_GOOGLE.SELECT_WEB_APPLICATION_AS")}</li>
          <li>
            {t("SSO_SETUP_GUIDELINE_GOOGLE.ADD_YOUR_DOMAIN_IN", { b: t("SSO_SETUP_GUIDELINE_GOOGLE.AUTHORIZED_JAVASCRIPT_ORIGINS") })}<br />
            <code>https://yourdomain.com</code>
          </li>
          <li>
            {t("SSO_SETUP_GUIDELINE_GOOGLE.ADD_REDIRECT_URI")}
            <br />
            <code>https://yourdomain.com/login/callback</code>
          </li>
          <li>{t("SSO_SETUP_GUIDELINE_GOOGLE.CLICK_CREATE_AND_NOTE_THE")}</li>
        </ul>
      </div>
    ),
  },
  {
    id: "4",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">{t("SSO_SETUP_GUIDELINE_GOOGLE.CONFIGURE_IN_ADMIN_PANEL")}</h4>
        <p>
          {t("SSO_SETUP_GUIDELINE_GOOGLE.COPY_THE_FOLLOWING_VALUES_FROM")}
        </p>
        <ul className="mt-2 list-inside list-disc">
          <li>
            <b>{t("SSO_SETUP_GUIDELINE_GOOGLE.CLIENT_ID")}</b>
          </li>
          <li>
            <b>{t("SSO_SETUP_GUIDELINE_GOOGLE.CLIENT_SECRET")}</b>
          </li>
          <li>
            <b>{t("SSO_SETUP_GUIDELINE_GOOGLE.REDIRECT_URI")}</b>: <code>https://yourdomain.com/login/callback</code>
          </li>
        </ul>
      </div>
    ),
  },
  {
    id: "5",
    description: (
      <div>
        <h4 className="text-lg font-semibold text-high-emphasis">{t("SSO_SETUP_GUIDELINE_GOOGLE.TEST_CONNECTION")}</h4>
        <p>{t("SSO_SETUP_GUIDELINE_GOOGLE.LOG_OUT_AND_TRY_LOGGING")}</p>
      </div>
    ),
  },
];
