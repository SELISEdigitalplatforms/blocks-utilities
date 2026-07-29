import { useSearchParams } from "react-router";
import { SignupEmailSent } from "@blocks-idp/authentication/pages/signup-email-sent";

export default function SignupEmailSentPage() {
  const [searchParams] = useSearchParams();
  const email = searchParams.get("email") ?? "";

  return <SignupEmailSent email={email} />;
}
