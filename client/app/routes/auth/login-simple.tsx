import { useState } from "react";
import { BlocksLoginPage } from "@/components/blocks-login-page";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { showErrorToast } from "@/hooks/use-toast";
import { API_BASES } from "@/constants/endpoint.constant";

export default function LoginSimplePage() {
  const [isStarting, setIsStarting] = useState(false);

  const startLogin = async () => {
    try {
      if (isStarting) return;
      setIsStarting(true);

      const blocksKey = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY");
      const clientId = getRuntimeEnv("BLOCKS_OIDC_CLIENT_ID");
      const redirectUri = `${window.location.origin}/login/callback`;
      const initiateUrl = `${API_BASES.IDP}/idp/initiate?x-blocks-key=${blocksKey}&clientId=${clientId}&redirectUri=${redirectUri}`;
      const headers: Record<string, string> = {};
      if (blocksKey) headers["X-Blocks-Key"] = blocksKey;

      const response = await fetch(initiateUrl.toString(), { headers });
      const data = await response.json();

      if (data.redirect_uri) {
        window.location.href = data.redirect_uri;
      } else {
        showErrorToast({ errors: "Failed to get authorization URL" });
        setIsStarting(false);
      }
    } catch (errors) {
      console.error("Login initiation error:", errors);
      showErrorToast({ errors: "Unable to start login. Please try again." });
      setIsStarting(false);
    }
  };

  return (
    <BlocksLoginPage
      name="blocks-utilities"
      onLogin={startLogin}
      isLoading={isStarting}
    />
  );
}
