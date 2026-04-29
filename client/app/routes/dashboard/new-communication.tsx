import StepHorizontalTrackBar from "@/components/stepper/horizontal-track-bar";
import StepVerticalTrackBar from "@/components/stepper/vertical-track-bar";
import { Button } from "@/components/ui-kits/button/button";
import { useStepper } from "@/components/stepper/stepper-provider";
import useIsMobile from "@/hooks/use-is-mobile";
import { X } from "lucide-react";
import BasicInformation from "@blocks-communication/mail/components/email-service/basic-information/basic-information";
import BeePluginStarter from "@blocks-communication/mail/components/bee-plugin-starter/bee-plugin-starter";
import { Link, useNavigate } from "react-router-dom";
import { useRef, useState } from "react";
import { blankTemplate } from "@blocks-communication/mail/constants/email-template";
import { useSaveMailTemplate } from "@blocks-communication/mail/hooks/use-email-template";
import { IEmailTemplate } from "@blocks-communication/mail/models/email";
import { useProjectStore } from "@/store/useProjectStore";
import { toast } from "@/hooks/use-toast";
import StepperProvider from "@/components/stepper/stepper-provider";
import { Steps } from "@/components/stepper/stepper-models";

const stepData: Steps = [
  { id: 1, title: "Basic information" },
  { id: 2, title: "Template" },
];

function NewCommunicationContent() {
  const { currentStep, nextStep, totalSteps } = useStepper();
  const { isPending, mutateAsync: saveTemplate } = useSaveMailTemplate();
  const ref = useRef<{ submit: () => void; isValid: boolean }>();
  const beeRef = useRef<{ submit: () => void; preview: () => void }>();
  const [templateData, setTemplateData] = useState<IEmailTemplate>({
    itemId: "",
  });
  const [isFormValid, setIsFormValid] = useState(false);
  const navigate = useNavigate();
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  const formSubmitHandler = async (data: IEmailTemplate) => {
    try {
      data.itemId = templateData?.itemId || "";
      const payload = {
        ...data,
        projectKey: tenantId,
      };
      const response = await saveTemplate(payload);
      data.itemId = response.itemId;
      setTemplateData(data);
      nextStep();
    } catch (error) {
      console.log(error);
    }
  };

  const handleBeePluginData = async (data: {
    htmlFile: string;
    jsonFile: string;
  }) => {
    console.log("newsletter-template.html", data.htmlFile);
    console.log("newsletter-template.html", data.jsonFile);
    const currentData: IEmailTemplate = {
      itemId: templateData?.itemId || "",
      templateBody: data.htmlFile,
      jsonContent: data.jsonFile,
    };
    try {
      const payload = {
        ...currentData,
        projectKey: tenantId,
      };
      const res = await saveTemplate(payload);
      setTemplateData(currentData);
      if (res.isSuccess) {
        navigate(`/email/communications/${res.itemId}`);
      } else {
        toast({
          variant: "destructive",
          title: "Error",
          description: JSON.stringify(res.errors),
        });
        navigate("/email");
      }
    } catch (error) {
      toast({
        variant: "destructive",
        title: "Error",
        description: "Something went wrong",
      });
      navigate("/email");
    }
  };

  return (
    <>
      <div className="flex px-10">
        <div className="hidden min-h-screen max-w-80 flex-col gap-5 bg-background p-5 pt-24 md:flex">
          <div className="mx-2 my-3">
            <div className="flex gap-2">
              <Link to="/email">
                <X size={32} strokeWidth={1} />
              </Link>
              <p className="mt-[2px] text-lg font-semibold">New Template</p>
            </div>
            <p className="mb-7 mt-2 text-sm font-normal text-medium-emphasis">
              Create a new template
            </p>
          </div>
          <StepVerticalTrackBar />
        </div>

        <div
          className={`ml-0 flex-1 py-5 sm:ml-5 ${useIsMobile() ? "mt-16" : ""}`}
        >
          <div className="flex flex-col items-center justify-center md:hidden">
            <div className="flex gap-2">
              <Link to="/email">
                <X size={32} strokeWidth={1} />
              </Link>
              <p className="mt-[2px] text-lg font-semibold">New Template</p>
            </div>
            <p className="mt-2 text-sm text-[#555]">Create a new template</p>
          </div>
          <div className="mt-8 w-full flex-row flex-wrap justify-between md:hidden">
            <StepHorizontalTrackBar />
          </div>
          <div className="grid w-full grid-cols-1 items-start">
            {currentStep === 1 ? (
              <BasicInformation
                onSubmit={formSubmitHandler}
                templateData={templateData}
                onValidityChange={setIsFormValid}
                ref={ref}
              />
            ) : (
              <div>
                <div className="mb-[20px] mt-[16px] flex items-center justify-between">
                  <h3 className="text-3xl font-semibold tracking-tight">
                    Template
                  </h3>
                  <div className="flex gap-2">
                    <Button
                      variant="outline"
                      size="lg"
                      className="gap-1 text-sm font-medium"
                      onClick={() => beeRef?.current?.preview()}
                    >
                      <span className="sr-only sm:not-sr-only">Preview</span>
                    </Button>
                    <Button
                      disabled={isPending}
                      size="lg"
                      onClick={() => {
                        beeRef?.current?.submit();
                      }}
                    >
                      Save
                    </Button>
                  </div>
                </div>
                <BeePluginStarter
                  onBeeSave={handleBeePluginData}
                  ref={beeRef}
                  jsonFile={blankTemplate}
                />
              </div>
            )}
          </div>

          <div className="mt-10">
            {currentStep === 1 ? (
              <Button
                size="lg"
                onClick={() => {
                  ref?.current?.submit();
                }}
                disabled={
                  currentStep === totalSteps || isPending || !isFormValid
                }
              >
                Save & Continue
              </Button>
            ) : (
              <div></div>
            )}
          </div>
        </div>
      </div>
    </>
  );
}

export default function NewCommunication() {
  return (
    <StepperProvider steps={stepData}>
      <NewCommunicationContent />
    </StepperProvider>
  );
}
