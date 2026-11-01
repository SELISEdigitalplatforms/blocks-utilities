import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";
import { ReactNode } from "react";
import type { TFunction } from "@seliseblocks/genesis-os";
import { getGoogleStepupDocs } from "./sso-setup-guideline-google";
import { getMicrosoftStepupDocs } from "./sso-setup-guideline-microsoft";
import { getGithubStepupDocs } from "./sso-setup-guideline-github";
import { getLinkedinStepupDocs } from "./sso-setup-guideline-linkedin";
import { getXStepupDocs } from "./sso-setup-guideline-x";

export type Step = {
  id: string;
  description: ReactNode;
};

export const getSsoSetupGuideSteps = (t: TFunction): Record<SSO_PROVIDERS, Step[]> => ({
  google: getGoogleStepupDocs(t),
  github: getGithubStepupDocs(t),
  linkedin: getLinkedinStepupDocs(t),
  x: getXStepupDocs(t),
  microsoft: getMicrosoftStepupDocs(t),
  apple: [],
  facebook: [],
  ownsso: [
    {
      id: "0",
      description: (
        <div>
          <p>
            {t("SSO_SETUP_GUIDELINE_STEPS_DOCS.COMING_SOON_THE_OWNSSO_SOCIAL")}
          </p>
          <p className="mt-2">
            {t("SSO_SETUP_GUIDELINE_STEPS_DOCS.COMING_SOON_THE_OWNSSO_SOCIAL")}
          </p>
        </div>
      ),
    },
  ],
});
