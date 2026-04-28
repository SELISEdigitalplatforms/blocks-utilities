import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";
import { ReactNode } from "react";
import { googleStepupDocs } from "./sso-setup-guideline-google";
import { microsoftStepupDocs } from "./sso-setup-guideline-microsoft";
import { githubStepupDocs } from "./sso-setup-guideline-github";
import { linkedinStepupDocs } from "./sso-setup-guideline-linkedin";
import { xStepupDocs } from "./sso-setup-guideline-x";

export type Step = {
  id: string;
  description: ReactNode;
};

export const SSOSetupGuideSteps: Record<SSO_PROVIDERS, Step[]> = {
  google: googleStepupDocs,
  github: githubStepupDocs,
  linkedin: linkedinStepupDocs,
  x: xStepupDocs,
  microsoft: microsoftStepupDocs,
  apple: [],
  facebook: [],
  ownsso: [
    {
      id: "0",
      description: (
        <div>
          <p>
            Coming soon: The OwnSSO social connection allows users to log in to your application using their OwnSSO
            account profile.
          </p>
          <p className="mt-2">
            Coming soon: The OwnSSO social connection allows users to log in to your application using their OwnSSO
            account profile.
          </p>
        </div>
      ),
    },
  ],
};
