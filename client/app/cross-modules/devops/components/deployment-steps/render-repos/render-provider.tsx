import { providers } from "@/cross-modules/devops/models/git-dummy";
import { useState } from "react";
import {
  authenticateWithGithub,
  authenticateWithGitlab,
  authenticateWithBitbucket,
  authenticateWithAzure,
  authenticateWithAws,
} from "@/cross-modules/devops/services/providers.service";
import { iconMap } from "@/cross-modules/devops/models/github-info";
import { Button } from "@/components/ui-kits/button/button";
import { useNavigate } from "react-router-dom";
import { useValidateAuthorization } from "@/cross-modules/devops/hooks/github-info";
import { IProviderDestination } from "@/cross-modules/devops/models/utils";
import { useProjectStore } from "@/store/useProjectStore";

interface ProviderButtonsProps extends IProviderDestination {
  onClose?: (verifyAuth?: boolean) => void | Promise<void>;
  extraState?: string;
  closeOnProviderSelect?: boolean;
}

const ProviderButtons = ({
  destination,
  onClose,
  extraState,
  closeOnProviderSelect,
}: ProviderButtonsProps) => {
  const navigate = useNavigate();
  const projectKey = useProjectStore().selectedProject?.tenantId || "";

  const { data: verifyAuth } = useValidateAuthorization();
  const [, setSelectedProvider] = useState<string | null>(null);
  
  const targetDestination = destination || "/devops/configure";
  
  if (destination) {
    localStorage.setItem("destination", destination);
  }

  const handleContinue = (providerId: string) => {
    setSelectedProvider(providerId);

    switch (providerId) {
      case "github":
        if (verifyAuth?.isSuccess) {
          if (onClose) {
            onClose(verifyAuth.isSuccess);
          } else {
            navigate(targetDestination);
          }
        } else {
          const reloadListener = (event: StorageEvent) => {
            if (event.key === "isReload" && event.newValue === "true") {
              window.removeEventListener("storage", reloadListener);
              localStorage.setItem("isReload", "false");
              if (onClose) onClose(true);
            }
          };
          window.addEventListener("storage", reloadListener);

          authenticateWithGithub(extraState || "", projectKey);
        }
        break;
      case "gitlab":
        authenticateWithGitlab();
        break;
      case "bitbucket":
        authenticateWithBitbucket();
        break;
      case "azure":
        authenticateWithAzure();
        break;
      case "aws":
        authenticateWithAws();
        break;
      default:
        console.error("Unknown provider:", providerId);
    }
    if (closeOnProviderSelect && onClose) onClose();
  };

  return (
    <>
      <div className="flex h-auto w-full flex-col items-center self-stretch">
        <div className="flex flex-col items-center gap-4">
          {providers.map((provider) => {
            const iconSrc = iconMap[provider.icon];
            return (
              <Button
                key={provider.id}
                onClick={() => handleContinue(provider.id)}
                className={`flex w-[345px] items-center justify-center gap-2 rounded border border-border bg-background px-4 py-2 font-medium transition-colors duration-200 hover:bg-secondary ${provider.name.toLowerCase()}`}
                type="button"
                disabled={!provider.active}
              >
                <img
                  src={iconSrc}
                  alt={`${provider.name} icon`}
                  width={18}
                  height={18}
                  className="object-contain"
                />
                <span className="text-foreground">Continue with {provider.name}</span>
              </Button>
            );
          })}
        </div>
      </div>
    </>
  );
};

export default ProviderButtons;
