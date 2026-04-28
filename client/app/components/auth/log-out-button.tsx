import { Button } from "@/components/ui-kits/button/button";
import { useLanguageViewStore } from "@/cross-modules/localization/store/use-language-view-store";
import { useLogout } from "@/idp/authentication/hooks/use-auth";
import { getQueryClient } from "@/providers/query-provider";
import { useAuthStore } from "@/store/useAuthStore";
import { useProjectStore } from "@/store/useProjectStore";

export function LogOutButton() {
  const queryClient = getQueryClient();
  const { reset } = useProjectStore();
  const { setUnAuthenticated, clearTokens } = useAuthStore();
  const { resetSelectedLanguages } = useLanguageViewStore();
  const { isPending, mutateAsync } = useLogout();

  const handleLogout = async () => {
    try {
      await mutateAsync();
      reset();
      setUnAuthenticated();
      clearTokens(); // Clear tokens for localhost
      resetSelectedLanguages();
      queryClient.clear();
      window.location.replace(`${window.location.origin}/login`);
    } catch (error) {
      console.error(error);
    }
  };

  return (
    <Button
      variant="link"
      size="sm"
      className="flex h-full w-full justify-start !p-0 text-error hover:no-underline"
      disabled={isPending}
      onClick={handleLogout}
    >
      Logout
    </Button>
  );
}