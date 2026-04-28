import { useStepper } from "@/components/stepper/stepper-provider";
import { Button } from "@/components/ui-kits/button/button";
import { cn } from "@/lib/utils";
import { Check } from "lucide-react";

const StepHorizontalTrackBar = () => {
  const { currentStep, completedSteps, goToStep, getSteps } = useStepper();
  const steps = getSteps();
  return (
    <div className="flex w-full flex-row flex-wrap justify-between">
      {steps.map((step, i) => {
        return (
          <div
            className={cn(
              "after:border-t-1 relative flex items-center after:h-0.5 after:border-gray-500 after:bg-gray-300 after:content-[''] [&:not(:last-child)]:flex-1 [&:not(:last-child)]:after:me-[35px] [&:not(:last-child)]:after:ms-[35px] [&:not(:last-child)]:after:flex-1",
              "data-[completed=true]:after:border-gray-700 data-[completed=true]:after:bg-gray-700",
            )}
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
              <div className="hidden md:block">{step.title}</div>
            </div>
          </div>
        );
      })}
    </div>
  );
};

export default StepHorizontalTrackBar;
