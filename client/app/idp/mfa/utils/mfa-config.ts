import { Mail, Smartphone } from "lucide-react";
import { tStatic } from "@seliseblocks/genesis-os";

export const getMFA_Provider_Data = () => [
  {
    provider: "email",
    label: tStatic("utilities.idp", "common:EMAIL"),
    Icon: Mail,
    status: false,
    type: 2,
    description: tStatic("utilities.idp", "MFA_CONFIG.RECEIVE_A_VERIFICATION_CODE_IN"),
  },
  {
    provider: "authenticator_app",
    label: tStatic("utilities.idp", "MFA_CONFIG.AUTHENTICATOR_APP"),
    Icon: Smartphone,
    status: false,
    type: 1,
    description:
      tStatic("utilities.idp", "MFA_CONFIG.USE_AN_AUTHENTICATION_APP_OR"),
  },
];
