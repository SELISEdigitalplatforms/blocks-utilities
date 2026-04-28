import { Outlet, useLocation, useSearchParams } from "react-router-dom";
import { createContext, useContext, useState, useEffect, ReactNode } from "react";
import { Logo } from "@/components/logo";
import { Loader } from "lucide-react";
import { extractOIDCParams } from "@blocks-idp/authentication/utils/oidc-utils";

type OIDCContextType = {
  logoUrl?: string;
  themeColor?: string;
  projectKey?: string;
  userName?: string;
  clientId?: string;
  redirectUri?: string;
  scope?: string;
  state?: string;
  nonce?: string;
  isLoading?: boolean;
};

const OIDCContext = createContext<OIDCContextType | undefined>(undefined);

export function useOIDCContext() {
  const context = useContext(OIDCContext);

  if (!context) {
    throw new Error("useOIDCContext must be used within OIDCProvider");
  }

  return {
    logoUrl: context.logoUrl,
    themeColor: context.themeColor || "#124091",
    projectKey: context.projectKey,
    userName: context.userName,
    clientId: context.clientId,
    redirectUri: context.redirectUri,
    state: context.state,
    scope: context.scope,
    nonce: context.nonce,
    isLoading: context.isLoading || false,
  };
}

function OIDCProvider({ children }: { children: ReactNode }) {
  const location = useLocation();
  const [searchParams] = useSearchParams();

  const [params, setParams] = useState<OIDCContextType>({
    themeColor: "#124091",
    isLoading: true,
  });

  useEffect(() => {
    const urlParams = extractOIDCParams(true);

    let stored: OIDCContextType = {};
    try {
      const storedStr = localStorage.getItem("oidc-flow-params");
      if (storedStr) {
        stored = JSON.parse(storedStr);
      }
    } catch (e) {
      console.error("Failed to parse stored params:", e);
    }

    const mergedParams: OIDCContextType = {
      projectKey: urlParams.projectKey || stored.projectKey,
      userName: urlParams.userName || stored.userName,
      logoUrl: urlParams.logoUrl || stored.logoUrl,
      themeColor: urlParams.themeColor || stored.themeColor || "#124091",
      clientId: urlParams.clientId || stored.clientId,
      redirectUri: urlParams.redirectUri || stored.redirectUri,
      state: urlParams.state || stored.state,
      scope: urlParams.scope || stored.scope,
      nonce: urlParams.nonce || stored.nonce,
      isLoading: false,
    };

    const hasAnyParams = Object.values(mergedParams).some(
      (value) => value && value !== "#124091",
    );

    if (hasAnyParams) {
      localStorage.setItem("oidc-flow-params", JSON.stringify(mergedParams));
    }

    setParams(mergedParams);
  }, [location.pathname, searchParams]);

  return <OIDCContext.Provider value={params}>{children}</OIDCContext.Provider>;
}

function OidcLayoutContent({ children }: { children: ReactNode }) {
  const { logoUrl, themeColor, isLoading } = useOIDCContext();

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <Loader className="h-12 w-12 animate-spin text-gray-500" />
      </div>
    );
  }

  return (
    <div
      className="flex min-h-screen flex-col items-center py-[24px] lg:py-[64px] xl:px-[154px]"
      style={{ "--theme-color": themeColor } as React.CSSProperties}
    >
      <div className="flex w-full items-center justify-center">
        <Logo
          src={logoUrl || "/Logo.svg"}
          alt="OIDC Logo"
          width={128}
          height={55}
          className="max-h-[55px] max-w-[128px]"
        />
      </div>
      <div className="mt-[20px] flex w-full flex-col justify-center gap-0 md:px-[24px] lg:mt-[70px] lg:flex-row lg:gap-20 lg:px-0 2xl:mt-[80px]">
        <div>
          <Outlet />
        </div>
      </div>
    </div>
  );
}

export function OidcLayout() {
  return (
    <OIDCProvider>
      <OidcLayoutContent>
        <Outlet />
      </OidcLayoutContent>
    </OIDCProvider>
  );
}

export { OIDCProvider };
