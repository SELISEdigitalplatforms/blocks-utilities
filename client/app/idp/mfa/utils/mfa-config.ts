import { Mail, Smartphone } from "lucide-react";

export const MFA_Provider_Data = [
  {
    provider: "email",
    label: "Email",
    Icon: Mail,
    status: false,
    type: 2,
    description: "Receive a verification code in your registered email to complete sign-in.",
  },
  {
    provider: "authenticator_app",
    label: "Authenticator app",
    Icon: Smartphone,
    status: false,
    type: 1,
    description:
      "Use an authentication app or browser extension to get two-factor authentication codes when prompted.",
  },
];
