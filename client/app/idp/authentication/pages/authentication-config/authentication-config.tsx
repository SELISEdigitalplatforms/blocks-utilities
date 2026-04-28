
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { LogMenu } from "@blocks-lmt/components";
import { TabsContent } from "@radix-ui/react-tabs";
import { useQueryState } from "nuqs";
import { getApiUrl } from "@/lib/get-api-path";
import { GrantTypes } from "./general/grant-types";
// import { SelfSignup } from "./general/self-signup";
import { GeneralSettings } from "./general/settings";
import { Button } from "@/components/ui-kits/button/button";
import { AuthenticationTabs } from "@blocks-idp/authentication/constants/authentication.constant";
// import { ClientCredentials } from "@blocks-idp/authentication/components/client-credentials";
// import { CreateClientCredential } from "@blocks-idp/authentication/components/create-client-credential";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui-kits/select/select";
import { Permissions } from "@blocks-idp/iam/modules/permission-management";
import { AddRole, Roles } from "@blocks-idp/iam/modules/role-management";
import { PrimaryButton } from "@/components/action-buttons/primary-button";
import { Link } from "react-router-dom";
import { useNavigate } from "react-router-dom";
import { CirclePlus, Settings, X } from "lucide-react";
import { EmailServiceTable, EmailConfiguration, EmailCommunicationDetails } from "@blocks-communication/mail";
import { useState, useRef } from "react";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui-kits/dialog/dialog";
import { Sheet, SheetContent } from "@/components/ui-kits/sheet/sheet";
import StepperProvider, { useStepper } from "@/components/stepper/stepper-provider";
import StepVerticalTrackBar from "@/components/stepper/vertical-track-bar";
import StepHorizontalTrackBar from "@/components/stepper/horizontal-track-bar";
import BasicInformation from "@blocks-communication/mail/components/email-service/basic-information/basic-information";
import BeePluginStarter from "@blocks-communication/mail/components/bee-plugin-starter/bee-plugin-starter";
import { useSaveMailTemplate } from "@blocks-communication/mail/hooks/use-email-template";
import { useProjectStore } from "@/store/useProjectStore";
import { IEmailTemplate } from "@blocks-communication/mail/models/email";
import { blankTemplate } from "@blocks-communication/mail/constants/email-template";

const NEW_COMMUNICATION_STEPS = [
  { id: 1, title: "Basic Information" },
  { id: 2, title: "Template" },
];

interface NewCommunicationContentProps {
  onClose: () => void;
  onCreated: (id: string) => void;
}

function NewCommunicationContent({ onClose, onCreated }: NewCommunicationContentProps) {
  const { currentStep, nextStep } = useStepper();
  const [templateData, setTemplateData] = useState<IEmailTemplate>({ itemId: "" });
  const [isFormValid, setIsFormValid] = useState(false);
  const { isPending, mutateAsync: saveTemplate } = useSaveMailTemplate();
  const ref = useRef<{ submit: () => void; isValid: boolean }>(null);
  const beeRef = useRef<{ submit: () => void; preview: () => void }>(null);
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  const formSubmitHandler = async (data: IEmailTemplate) => {
    try {
      data.itemId = templateData?.itemId || "";
      const response = await saveTemplate({ ...data, projectKey: tenantId });
      data.itemId = response.itemId;
      setTemplateData(data);
      nextStep();
    } catch (error) {
      console.log(error);
    }
  };

  const handleBeePluginData = async (data: { htmlFile: string; jsonFile: string }) => {
    try {
      const res = await saveTemplate({
        itemId: templateData?.itemId || "",
        templateBody: data.htmlFile,
        jsonContent: data.jsonFile,
        projectKey: tenantId,
      });
      if (res.isSuccess) {
        onCreated(res.itemId);
      }
    } catch (error) {
      console.log(error);
    }
  };

  return (
    <div className="flex h-full">
      {/* Sidebar (desktop) */}
      <div className="hidden min-h-full w-64 flex-shrink-0 flex-col gap-5 border-r bg-background p-5 pt-10 md:flex">
        <div className="mx-2 my-3">
          <div className="flex items-center gap-2">
            <Button size="icon" variant="ghost" className="h-8 w-8" onClick={onClose}>
              <X className="h-6 w-6" />
            </Button>
            <p className="text-lg font-semibold">New Template</p>
          </div>
          <p className="mb-7 mt-2 text-sm font-normal text-medium-emphasis">Create a new template</p>
        </div>
        <StepVerticalTrackBar />
      </div>

      {/* Main content */}
      <div className="flex-1 overflow-auto p-6">
        {/* Mobile header */}
        <div className="mb-4 flex flex-col items-center justify-center md:hidden">
          <div className="flex items-center gap-2">
            <Button size="icon" variant="ghost" className="h-8 w-8" onClick={onClose}>
              <X className="h-6 w-6" />
            </Button>
            <p className="text-lg font-semibold">New Template</p>
          </div>
          <p className="mt-2 text-sm text-medium-emphasis">Create a new template</p>
          <div className="mt-4 w-full">
            <StepHorizontalTrackBar />
          </div>
        </div>

        {currentStep === 1 ? (
          <div className="[&>main]:mt-0 [&>main]:sm:mt-0">
            <BasicInformation
              ref={ref}
              onSubmit={formSubmitHandler}
              templateData={templateData}
              onValidityChange={setIsFormValid}
            />
          </div>
        ) : (
          <div>
            <div className="mb-5 flex items-center justify-between">
              <h3 className="text-2xl font-semibold">Template</h3>
              <div className="flex gap-2">
                <Button variant="outline" onClick={() => beeRef?.current?.preview()}>
                  Preview
                </Button>
                <Button disabled={isPending} onClick={() => beeRef?.current?.submit()}>
                  Save
                </Button>
              </div>
            </div>
            <BeePluginStarter ref={beeRef} onBeeSave={handleBeePluginData} jsonFile={blankTemplate} />
          </div>
        )}

        {currentStep === 1 && (
          <div className="mt-10">
            <Button
              size="lg"
              onClick={() => ref?.current?.submit()}
              disabled={isPending || !isFormValid}
            >
              Save & Continue
            </Button>
          </div>
        )}
      </div>
    </div>
  );
}

export const AuthenticationConfig = () => {
  const [selectedTab, setSelectedTab] = useQueryState("tab", { defaultValue: "general" });
  const navigate = useNavigate();
  const [configureOpen, setConfigureOpen] = useState(false);
  const [addTemplateOpen, setAddTemplateOpen] = useState(false);
  const [selectedTemplateId, setSelectedTemplateId] = useState<string | null>(null);

  const handleTemplateCreated = (id: string) => {
    setAddTemplateOpen(false);
    setSelectedTemplateId(id);
  };
  return (
    <div>
      <div className="mb-[18px] flex items-center justify-between md:mb-[24px]">
        <h1 className="text-lg font-semibold md:text-2xl">IDP</h1>
    
      </div>
      <Tabs value={selectedTab} onValueChange={(value) => setSelectedTab(value)}>
        <div className="mb-4 flex items-start justify-between gap-4">
          <>
            <div className="hidden w-full overflow-x-auto sm:block [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
              <TabsList className="w-max">
                {AuthenticationTabs.map((item) => (
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
                  {AuthenticationTabs.map((item) => (
                    <SelectItem key={item.id} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </>

          <>
            {/* {selectedTab === GRANT_TYPES.clientCredential && <CreateClientCredential />} */}
            {selectedTab === "roles" && <AddRole />}
            {selectedTab === "permissions" && (
              <Link to="/services/iam/permission-detail/new">
                <PrimaryButton label="Add Permission" />
              </Link>
            )}
            {selectedTab === "email-template" && (
              <div className="flex shrink-0 items-center gap-2">
                <Button
                  variant="outline"
                  size="default"
                  className="gap-1 text-sm font-medium"
                  onClick={() => setConfigureOpen(true)}
                >
                  <Settings className="h-5 w-5" />
                  <span className="sr-only sm:not-sr-only">Configure</span>
                </Button>
                <Button
                  size="default"
                  variant="default"
                  className="bg-primary text-primary-foreground shadow-none"
                  onClick={() => setAddTemplateOpen(true)}
                >
                  <CirclePlus className="h-5 w-5 lg:mr-2" />
                  <span className="sr-only lg:not-sr-only">Add Template</span>
                </Button>
              </div>
            )}
          </>
        </div>
        <TabsContent value="general" className="grid grid-cols-1 gap-6">
          <GeneralSettings />
          <GrantTypes />
          {/* <SelfSignup /> */}
        </TabsContent>
        <TabsContent value="signin-flow">
          <div className="rounded-lg border border-border bg-card p-6">
            <h3 className="text-lg font-semibold">Signin flow</h3>
            <p className="text-muted-foreground mt-2">Configure your signin flow settings</p>
          </div>
        </TabsContent>
        <TabsContent value="signup-flow">
          <div className="rounded-lg border border-border bg-card p-6">
            <h3 className="text-lg font-semibold">Signup flow</h3>
            <p className="text-muted-foreground mt-2">Configure your signup flow settings</p>
          </div>
        </TabsContent>
        <TabsContent value="email-template">
          {selectedTemplateId ? (
            <EmailCommunicationDetails
              params={{ id: selectedTemplateId }}
              onBack={() => setSelectedTemplateId(null)}
            />
          ) : (
            <EmailServiceTable onRowClick={(id) => setSelectedTemplateId(String(id))} />
          )}
        </TabsContent>
        <TabsContent value="oidc-template">
          <div className="rounded-lg border border-border bg-card p-6">
            <h3 className="text-lg font-semibold">OIDC template</h3>
            <p className="text-muted-foreground mt-2">Configure your OIDC template settings</p>
          </div>
        </TabsContent>
        <TabsContent value="roles">
          <Roles />
        </TabsContent>
        <TabsContent value="permissions">
          <Permissions />
        </TabsContent>
        {/* <TabsContent value={GRANT_TYPES.clientCredential}>
          <ClientCredentials />
        </TabsContent> */}
      </Tabs>

      {/* Configure dialog */}
      <Dialog open={configureOpen} onOpenChange={setConfigureOpen}>
        <DialogContent className="max-h-[90vh] max-w-3xl overflow-y-auto">
          <DialogHeader>
            <DialogTitle>Email Configuration</DialogTitle>
          </DialogHeader>
          <EmailConfiguration />
        </DialogContent>
      </Dialog>

      {/* New template sheet */}
      <Sheet open={addTemplateOpen} onOpenChange={setAddTemplateOpen}>
        <SheetContent side="right" className="flex h-full w-full max-w-full flex-col overflow-hidden p-0 sm:max-w-full" hideClose>
          <StepperProvider steps={NEW_COMMUNICATION_STEPS}>
            <NewCommunicationContent
              onClose={() => setAddTemplateOpen(false)}
              onCreated={handleTemplateCreated}
            />
          </StepperProvider>
        </SheetContent>
      </Sheet>
    </div>
  );
};
