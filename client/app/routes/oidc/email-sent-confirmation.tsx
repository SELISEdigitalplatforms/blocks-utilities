import { useSearchParams } from "react-router-dom";
import { OidcEmailConfirmation } from "@blocks-idp/authentication/pages/oidc/email-sent-confirmation/email-sent-confirmation";

export default function OidcEmailSentConfirmationPage() {
  const [searchParams] = useSearchParams();
  const email = searchParams.get("email") ?? "";

  return <OidcEmailConfirmation email={email} />;
}
