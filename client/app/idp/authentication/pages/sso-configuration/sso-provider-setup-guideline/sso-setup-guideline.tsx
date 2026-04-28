import { useMemo, useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { Progress } from "@/components/ui-kits/progress/progress";
import { Separator } from "@/components/ui-kits/separator/separator";

type Step = {
  id: string;
  description: React.ReactNode;
};

interface SSOSetupGuideLineProps {
  steps: Step[];
}

export const SSOSetupGuideLine = ({ steps }: SSOSetupGuideLineProps) => {
  const [stepIndex, setStepIndex] = useState(0);

  const isFirst = stepIndex === 0;
  const isLast = stepIndex === steps.length - 1;

  const progress = useMemo(() => {
    if (steps.length <= 1) return 100;
    return (stepIndex / (steps.length - 1)) * 100;
  }, [stepIndex, steps.length]);

  const goPrev = () => setStepIndex((index) => Math.max(index - 1, 0));
  const goNext = () => setStepIndex((index) => Math.min(index + 1, steps.length - 1));

  const currentStep = steps[stepIndex];

  return (
    <div className="flex h-full flex-col justify-between">
      <div className="flex-1 p-4 text-sm text-medium-emphasis">{currentStep.description}</div>

      <div>
        <Separator orientation="horizontal" />
        <div className="flex items-center justify-between gap-4 p-4">
          <div className="flex-1">
            <div className="text-sm text-medium-emphasis">YOUR PROGRESS</div>
            <Progress value={progress} className="mt-1 h-3 w-full" />
          </div>
          <div className="flex items-center gap-2">
            <Button variant="outline" className="p-2" onClick={goPrev} disabled={isFirst}>
              <ChevronLeft className="aspect-square w-4" />
            </Button>
            <Button variant="outline" className="p-2" onClick={goNext} disabled={isLast}>
              <ChevronRight className="aspect-square w-4" />
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
};
