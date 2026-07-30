import { useSearchParams } from "react-router";
import { ForgotEmailSent } from "@blocks-idp/authentication/pages/forgot-email-sent";

export default function ForgotEmailSentPage() {
  const [searchParams] = useSearchParams();
  const email = searchParams.get("email") ?? "";

  return <ForgotEmailSent email={email} />;
}
