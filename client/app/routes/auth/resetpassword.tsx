import { useSearchParams } from "react-router-dom";
import { ResetPassword } from "@blocks-idp/authentication/pages/reset-password";

export default function ResetPasswordPage() {
  const [searchParams] = useSearchParams();
  const code = searchParams.get("code") ?? undefined;
  const lang = searchParams.get("lang") ?? undefined;

  return <ResetPassword code={code} lang={lang} />;
}
