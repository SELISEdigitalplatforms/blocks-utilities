import { useEffect } from "react";
import { Link } from "react-router-dom";
import { X } from "lucide-react";
import { useQueryState } from "nuqs";
import StepVerticalTrackBar from "@/components/stepper/vertical-track-bar";
import StepHorizontalTrackBar from "@/components/stepper/horizontal-track-bar";
import StepperProvider, { useStepper } from "@/components/stepper/stepper-provider";
import { Steps } from "@/components/stepper/stepper-models";
import { useCreateProjectFormState } from "@/components/create-project/utils";
import { CreateProjectNamingForm } from "@/components/create-project/form/create-project-naming-form/create-project-naming-form";
import { CreateProjectResourcesForm } from "@/components/create-project/form/create-project-resources-form/create-project-resources-form";
import { CreateProjectEnvironmentsForm } from "@/components/create-project/form/create-project-environments-form/create-project-environments-form";

const stepData: Steps = [
  { id: 1, title: "Name your project" },
  { id: 2, title: "Add resources" },
  { id: 3, title: "Configure environments" },
];

export const CreateProjectWrapper = () => {
  return (
    <StepperProvider steps={stepData}>
      <CreateProject />
    </StepperProvider>
  );
};

const CreateProject = () => {
  const { resetFormData } = useCreateProjectFormState();
  const [tab, setTab] = useQueryState("tab", { defaultValue: "1" });
  const { currentStep, goToStep, setCompletedSteps } = useStepper();

  useEffect(() => {
    if (tab) {
      const step = parseInt(tab, 10);
      if (!isNaN(step) && step > 0 && step <= stepData.length && step === 2) {
        setCompletedSteps([1]);
        goToStep(step);
        setTab("0");
      }
    }
  }, [tab, goToStep, setCompletedSteps, setTab]);

  return (
    <>
      {/* mobile design */}
      <div className="flex flex-col md:hidden">
        <div className="mt-16 flex-1 p-5">
          <div className="flex flex-col items-center justify-center md:hidden">
            <div className="flex gap-2">
              <Link to="/console">
                <X onClick={resetFormData} size={32} strokeWidth={1} />
              </Link>
              <p className="mt-[2px] text-lg font-semibold">Create a project</p>
            </div>
            <p className="mb-7 mt-2 text-sm font-normal text-medium-emphasis">
              Let&apos;s get started by setting up the basics for your new project.
            </p>
            <StepHorizontalTrackBar />
          </div>
        </div>
        <div className="p-5">
          {currentStep === 1 && <CreateProjectNamingForm />}
          {currentStep === 2 && <CreateProjectResourcesForm />}
          {currentStep === 3 && <CreateProjectEnvironmentsForm />}
        </div>
      </div>
      {/* desktop design */}
      <div className="hidden gap-12 px-10 md:flex">
        <div className="min-h-screen max-w-80 gap-5 bg-background p-5 pt-24 dark:bg-gray-900">
          <div className="mx-2 my-3">
            <div className="flex gap-2">
              <Link to="/console">
                <X onClick={resetFormData} size={32} strokeWidth={1} />
              </Link>
              <p className="mt-[2px] text-lg font-semibold">Create a project</p>
            </div>
            <p className="mb-7 mt-2 text-sm font-normal text-medium-emphasis">
              Let&apos;s get started by setting up the basics for your new project.
            </p>
          </div>
          <StepVerticalTrackBar />
        </div>
        <div className="mt-24 w-full">
          {currentStep === 1 && <CreateProjectNamingForm />}
          {currentStep === 2 && <CreateProjectResourcesForm />}
          {currentStep === 3 && <CreateProjectEnvironmentsForm />}
        </div>
      </div>
    </>
  );
};
