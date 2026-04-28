import LoadingSpinner from "@/components/loader-spinner/loader-spinner";
import { cn } from "@/lib/utils";
import { LoginOption } from "@blocks-identifier/models/project.model";
import { SSOSigninCard } from "@blocks-idp/authentication/components/sso-signin-card";
import { SOCIAL_AUTH_PROVIDERS_CONFIG } from "@blocks-idp/authentication/constants/sso-providers.constant";
import { useSsoActivation } from "@blocks-idp/authentication/hooks/use-sso-activation";

type SsoSigninProps = {
  loginOption: LoginOption;
};

// Static map so Tailwind's JIT scanner can detect all class names at build
// time. Dynamic template literals like `grid-cols-${n}` are purged in prod.
const GRID_COLS_MAP: Record<number, string> = {
  1: "grid-cols-1",
  2: "grid-cols-2",
  3: "grid-cols-3",
  4: "grid-cols-4",
  5: "grid-cols-5",
  6: "grid-cols-3",
};

export const SsoSignin = ({ loginOption }: SsoSigninProps) => {
  const { isPending } = useSsoActivation();

  const providers = Object.values(SOCIAL_AUTH_PROVIDERS_CONFIG)
    .map((config) => {
      const sso = loginOption?.ssoInfo?.find(
        (s) => s.provider === config.provider && config.isAvailable,
      );
      if (!sso) return null;

      return {
        ...config,
        audience: sso.audience,
        provider: sso.provider,
      };
    })
    .filter((item) => !!item);

  const gridColsClass = GRID_COLS_MAP[Math.min(providers.length, 6)];

  return (
    <>
      <div className={cn("grid gap-2", providers.length > 2 && gridColsClass)}>
        {providers.map((item) => (
          <SSOSigninCard
            providerConfig={item}
            key={item.provider}
            withLabel={providers.length < 3}
          />
        ))}
      </div>

      {isPending && (
        <div className="fixed bottom-0 left-0 right-0 top-0 flex items-center justify-center bg-muted opacity-70">
          <LoadingSpinner size={48} color="text-primary" />
        </div>
      )}
    </>
  );
};
