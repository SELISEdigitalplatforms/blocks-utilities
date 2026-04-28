import { useStepper } from "@/components/stepper/stepper-provider";
import { Button } from "@/components/ui-kits/button/button";
import { Check } from "lucide-react";
import { cn } from "@/lib/utils";

const StepVerticalTrackBar = () => {
  const { currentStep, totalSteps, goToStep, completedSteps, getSteps } = useStepper();
  const steps = getSteps();
  return (
    <div>
      {steps.map((step, i) => {
        return (
          <div
            className={cn("relative flex flex-col [&:not(:last-child)]:flex-1")}
            key={step.id}
            data-completed={completedSteps[i] ? true : false}
          >
            <div className="flex items-center gap-2">
              <Button
                variant="ghost"
                className={cn(
                  "h-8 w-8 rounded-full border p-0 text-lg font-bold text-gray-300",
                  "data-[current=true]:border-gray-700 data-[current=true]:text-foreground",
                  "data-[completed=true]:border-gray-700 data-[completed=true]:text-foreground",
                )}
                data-current={currentStep - 1 === i}
                data-completed={completedSteps[i] ? true : false}
                onClick={() => goToStep(i + 1)}
              >
                {completedSteps[i] ? <Check size={18} /> : i + 1}
              </Button>
              <div
                className={cn(
                  "ml-4 text-base font-medium text-gray-300",
                  "data-[current=true]:text-foreground",
                  "data-[completed=true]:text-foreground",
                )}
                data-current={currentStep - 1 === i}
                data-completed={completedSteps[i] ? true : false}
              >
                {step.title}
              </div>
            </div>
            {i !== totalSteps - 1 && (
              <div
                className={cn(
                  "solid-line my-2 ml-4 h-11 w-[1px] bg-gray-300",
                  "data-[completed=true]:bg-gray-700",
                )}
                data-current={currentStep - 1 === i}
                data-completed={completedSteps[i] ? true : false}
              ></div>
            )}
          </div>
        );
      })}
    </div>
  );
};
export default StepVerticalTrackBar;
