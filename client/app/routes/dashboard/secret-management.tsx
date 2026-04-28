import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { TabsContent } from "@radix-ui/react-tabs";
import { useQueryState } from "nuqs";
import { SSO } from "@blocks-idp/authentication/pages/authentication-config/sso";
import { GRANT_TYPES, SecretManagementTabs } from "@blocks-idp/authentication/constants/authentication.constant";
import { AIModels } from "@blocks-ai/pages/aimodels";
import { OIDC } from "@blocks-idp/authentication/components/oidc";
import { Certificates } from "@blocks-idp/authentication/pages/authentication-config/general/certificates/certificates";
import { CreateOIDC } from "@blocks-idp/authentication/components/create-oidc";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui-kits/select/select";
import { ConfigureCaptcha } from "@blocks-idp/captcha/pages/configure-captcha";
import { ConfigureCaptchaModal } from "@blocks-idp/captcha/modals/configure-captcha-modal";
import { ConfigureMFA } from "@blocks-idp/mfa/pages/configure-mfa/configure-mfa";
import { MagicUrlConfigDialog } from "@blocks-utilities/components/magic-url-config-dialog/magic-url-config-dialog";
import { useSaveMagicUrlConfig } from "@blocks-utilities/hooks/use-magic-url";
import { StorageContents } from "@blocks-storage/pages/storage/storage-contents";
import { ManagedServices } from "@blocks-identifier/pages/services/managed-services";
import { AddService } from "@blocks-identifier/components/add-service/add-service";
import { EmailConfiguration } from "@blocks-communication/mail/email/email-configure/email-configure";
import NotificationConfigurationList from "@blocks-communication/notification/components/notification-configuration-list";
import { Button } from "@/components/ui-kits/button/button";
import { getApiUrl } from "@/lib/get-api-path";
import { CirclePlus, Settings, Notebook, AlertCircle } from "lucide-react";
import { MouseEvent, useMemo, useState } from "react";
import { CAPTCHA_PROVIDERS, CAPTCHA_PROVIDERS_KEY } from "@blocks-idp/captcha/models/captcha";
import { useGetCaptchaConfigs } from "@blocks-idp/captcha/hooks/use-captcha-config";
import { useProjectStore } from "@/store/useProjectStore";
import { DialogTrigger } from "@radix-ui/react-dialog";
import { toast } from "@/hooks/use-toast";

export default function SecretManagementPage() {
  const [selectedTab, setSelectedTab] = useQueryState("tab", { defaultValue: "infra-config" });
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data: captchaData } = useGetCaptchaConfigs({ projectKey: tenantId });
  const { mutateAsync: saveMagicUrlConfig } = useSaveMagicUrlConfig();
  const [isMagicUrlConfigDialogOpen, setIsMagicUrlConfigDialogOpen] = useState(false);
  const [isManagedServicesGuideOpen, setIsManagedServicesGuideOpen] = useState(false);
  const [isEmailConfigOpen, setIsEmailConfigOpen] = useState(false);
  const [isNotificationConfigOpen, setIsNotificationConfigOpen] = useState(false);

  const areAllProvidersConfigured = useMemo(() => {
    if (!captchaData?.configurations) return false;
    const allProviderKeys = Object.keys(CAPTCHA_PROVIDERS) as CAPTCHA_PROVIDERS_KEY[];
    const configuredProviders = new Set(
      captchaData.configurations.map((config: { provider: string }) => config.provider),
    );
    return allProviderKeys.every((key) => configuredProviders.has(key));
  }, [captchaData]);

  const addConfigurationHandler = (e: MouseEvent) => {
    if (areAllProvidersConfigured) {
      toast({
        variant: "info",
        title: "Info",
        description: "No additional captcha configurations can be added.",
      });
      return e.preventDefault();
    }
  };

  return (
    <div className="p-6">
      <div className="mb-[18px] flex items-center justify-between md:mb-[24px]">
        <h1 className="text-lg font-semibold md:text-2xl">Secrets & Configs</h1>
       
      </div>
      <Tabs value={selectedTab} onValueChange={(value) => setSelectedTab(value)}>
        <div className="mb-4 flex items-start justify-between gap-4">
          <>
            <div className="hidden w-full overflow-x-auto sm:block [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
              <TabsList className="w-max">
                {SecretManagementTabs.map((item) => (
                  <TabsTrigger key={item.id} value={item.value}>
                    {item.label}
                  </TabsTrigger>
                ))}
              </TabsList>
            </div>
            <div className="sm:hidden">
              <Select value={selectedTab} onValueChange={(value) => setSelectedTab(value)}>
                <SelectTrigger className="w-32 gap-2">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {SecretManagementTabs.map((item) => (
                    <SelectItem key={item.id} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </>
          <>
            {selectedTab === GRANT_TYPES.authorizationCode && <CreateOIDC />}
            {selectedTab === "captcha" && (
              <div className="flex items-center gap-2">
                <ConfigureCaptchaModal>
                  <DialogTrigger asChild>
                    <Button size="sm" onClick={addConfigurationHandler}>
                      <CirclePlus className="h-5 w-5" />
                      <span className="sr-only sm:not-sr-only sm:ml-2.5 sm:text-sm sm:whitespace-nowrap">Add Configuration</span>
                    </Button>
                  </DialogTrigger>
                </ConfigureCaptchaModal>
              </div>
            )}
            {selectedTab === "magic-url" && (
              <div className="flex items-center gap-2">
                <Button variant="outline" size="sm" onClick={() => setIsMagicUrlConfigDialogOpen(true)}>
                  <Settings className="h-5 w-5" />
                  <span className="sr-only sm:not-sr-only sm:ml-2.5 sm:text-sm sm:whitespace-nowrap">Configure</span>
                </Button>
                <MagicUrlConfigDialog
                  open={isMagicUrlConfigDialogOpen}
                  onOpenChange={setIsMagicUrlConfigDialogOpen}
                  projectKey={tenantId}
                  onSave={async (config) => { await saveMagicUrlConfig(config); }}
                />
              </div>
            )}
            {selectedTab === "managed-services" && (
              <div className="flex items-center gap-2">
                <Button variant="outline" size="sm" onClick={() => setIsManagedServicesGuideOpen(true)}>
                  <Notebook className="aspect-square w-4" />
                  <span className="sr-only sm:not-sr-only sm:ml-2 sm:text-sm sm:whitespace-nowrap">Setup Guide</span>
                </Button>
                <AddService />
              </div>
            )}
            {selectedTab === "email" && (
              <div className="flex items-center gap-2">
                <Button size="sm" onClick={() => setIsEmailConfigOpen(true)}>
                  <CirclePlus className="h-5 w-5" />
                  <span className="sr-only sm:not-sr-only sm:ml-2.5 sm:text-sm sm:whitespace-nowrap">Add Configuration</span>
                </Button>
              </div>
            )}
            {selectedTab === "notification" && (
              <div className="flex items-center gap-2">
                <Button size="sm" onClick={() => setIsNotificationConfigOpen(true)}>
                  <CirclePlus className="h-5 w-5" />
                <span className="sr-only sm:not-sr-only sm:ml-2.5 sm:text-sm sm:whitespace-nowrap">Add Configuration</span>
                </Button>
              </div>
            )}
          </>
        </div>

        {!["my-secret", "managed-services", "ai-models"].includes(selectedTab) && (
          <div className="mb-4 flex items-start gap-3 rounded-lg border border-amber-200 bg-amber-50 p-4 dark:border-amber-900/30 dark:bg-amber-950/20">
            <AlertCircle className="mt-0.5 h-5 w-5 flex-shrink-0 text-amber-600 dark:text-amber-500" />
            <div className="flex-1">
              <h4 className="font-semibold text-amber-900 dark:text-amber-100">Secret values are hidden for security</h4>
              <p className="mt-1 text-sm text-amber-800 dark:text-amber-200">
                Once you enter secret values, they won't be displayed again for security reasons. You can only view and manage configurations.
              </p>
            </div>
          </div>
        )}

        <TabsContent value="infra-config">
          <div className="rounded-lg border border-border bg-card p-6">
            <h3 className="text-lg font-semibold">Infra Config</h3>
            <p className="mt-2 text-muted-foreground">Manage your infrastructure configurations</p>
          </div>
        </TabsContent>
        <TabsContent value={GRANT_TYPES.authorizationCode}>
          <OIDC />
        </TabsContent>
        <TabsContent value="managed-services">
          <ManagedServices
            guideOpen={isManagedServicesGuideOpen}
            onGuideOpenChange={setIsManagedServicesGuideOpen}
          />
        </TabsContent>
        <TabsContent value="my-secret">
          <div className="rounded-lg border border-border bg-card p-6">
            <h3 className="text-lg font-semibold">My Secret</h3>
            <p className="mt-2 text-muted-foreground">Manage your secrets and credentials</p>
          </div>
        </TabsContent>
        <TabsContent value={GRANT_TYPES.social}>
          <SSO />
        </TabsContent>
        <TabsContent value="external-idp">
          <Certificates />
        </TabsContent>
        <TabsContent value="captcha">
          <ConfigureCaptcha />
        </TabsContent>
        <TabsContent value="mfa">
          <ConfigureMFA />
        </TabsContent>
        <TabsContent value="magic-url">
          <div className="rounded-lg border border-dashed p-8 text-center text-muted-foreground bg-background">
            <p>Use the Configure button above to manage Magic URL settings.</p>
          </div>
        </TabsContent>
        <TabsContent value="storage">
          <StorageContents />
        </TabsContent>
        <TabsContent value="email">
          <EmailConfiguration
            addConfigOpen={isEmailConfigOpen}
            onAddConfigOpenChange={setIsEmailConfigOpen}
          />
        </TabsContent>
        <TabsContent value="notification">
          <NotificationConfigurationList
            addConfigOpen={isNotificationConfigOpen}
            onAddConfigOpenChange={setIsNotificationConfigOpen}
          />
        </TabsContent>
        <TabsContent value="ai-models">
          <AIModels />
        </TabsContent>
      </Tabs>
    </div>
  );
}

