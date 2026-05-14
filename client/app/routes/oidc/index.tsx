import { useEffect, useState } from "react";
import { useSearchParams, useNavigate } from "react-router-dom";
import { OIDCPermissionWrapper } from "@blocks-idp/authentication/pages/oidc/permission-wrapper";
import { OIDCSignin } from "@blocks-idp/authentication/pages/oidc/oidc-signin";
import { authService } from "@blocks-idp/authentication/services/auth.service";
import { useAuthStore } from "@/store/useAuthStore";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { Loader } from "lucide-react";
import { debug } from "@/lib/debug";

export default function OidcIndexPage() {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { setAuthenticated, setTokens } = useAuthStore();
  const [isExchanging, setIsExchanging] = useState(false);

  const code = searchParams.get("code");
  const state = searchParams.get("state");
  const userName = searchParams.get("userName");

  debug.group("OidcIndexPage");
  debug.log("URL:", window.location.href);
  debug.log("code:", code, "state:", state, "userName:", userName);
  debug.dumpAuthState();
  debug.groupEnd();

  useEffect(() => {
    if (!code || !state) {
      debug.log("No code or state in URL, skipping token exchange");
      return;
    }

    debug.group("OidcIndexPage.useEffect - Token Exchange");
    debug.log("Exchanging code for tokens...");
    debug.dumpAuthState();

    setIsExchanging(true);
    authService.verifyOidc({ code, state })
      .then((res) => {
        debug.log("verifyOidc success, response:", res);
        const isLocalhost = getRuntimeEnv("BLOCKS_API_BASE_URL")?.includes("localhost");

        // Always store tokens in localStorage for http-client refresh token support
        if (res.access_token || res.refresh_token) {
          try {
            localStorage.setItem("oidc-auth-storage", JSON.stringify({
              access_token: res.access_token,
              refresh_token: res.refresh_token,
            }));
            debug.log("Stored tokens in localStorage");
          } catch (e) {
            debug.error("Failed to store tokens in localStorage", e);
          }
        }

        if (isLocalhost && res.access_token && res.refresh_token) {
          debug.log("localhost env - storing tokens in zustand");
          setTokens(res.access_token, res.refresh_token);
        }
        setAuthenticated();
        debug.log("setAuthenticated() called");
        debug.dumpAuthState();

        const redirectTo = `${window.location.origin}/email`;
        debug.log("Redirecting to:", redirectTo);
        debug.groupEnd();
        window.location.href = redirectTo;
      })
      .catch((err) => {
        debug.error("verifyOidc failed:", err);
        debug.groupEnd();
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
