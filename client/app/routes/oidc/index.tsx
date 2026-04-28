import { useEffect, useState } from "react";
import { useSearchParams, useNavigate } from "react-router-dom";
import { OIDCPermissionWrapper } from "@blocks-idp/authentication/pages/oidc/permission-wrapper";
import { OIDCSignin } from "@blocks-idp/authentication/pages/oidc/oidc-signin";
import { authService } from "@blocks-idp/authentication/services/auth.service";
import { useAuthStore } from "@/store/useAuthStore";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { Loader } from "lucide-react";

export default function OidcIndexPage() {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { setAuthenticated, setTokens } = useAuthStore();
  const [isExchanging, setIsExchanging] = useState(false);

  const code = searchParams.get("code");
  const state = searchParams.get("state");
  const userName = searchParams.get("userName");

  useEffect(() => {
    if (!code || !state) return;

    setIsExchanging(true);
    authService.verifyOidc({ code, state })
      .then((res) => {
        const isLocalhost = getRuntimeEnv("BLOCKS_API_BASE_URL")?.includes("localhost");
        
        if (isLocalhost && res.access_token && res.refresh_token) {
          setTokens(res.access_token, res.refresh_token);
        }
        setAuthenticated();

        window.location.href = `${window.location.origin}/console`;
      })
      .catch(() => {
        navigate("/oidc/error");
      })
      .finally(() => setIsExchanging(false));
  }, [code, state]);

  if (code && state) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <Loader className="h-12 w-12 animate-spin text-gray-500" />
      </div>
    );
  }

  if (userName && userName.trim() !== "") {
    return <OIDCPermissionWrapper />;
  }

  return <OIDCSignin />;
}
