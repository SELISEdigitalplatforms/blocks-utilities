import { useSearchParams, Navigate } from "react-router-dom";
import { SsoActivate } from "@blocks-idp/authentication/pages/sso-activate";

export default function SsoActivatePage() {
  const [searchParams] = useSearchParams();
  const code = searchParams.get("code");
  const username = searchParams.get("username");

  if (!code || !username) return <Navigate to="/login" replace />;

  return <SsoActivate oauthParams={{ code, username }} />;
}
