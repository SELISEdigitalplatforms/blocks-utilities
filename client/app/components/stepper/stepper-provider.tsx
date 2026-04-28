import { StepperContextType, Steps } from "./stepper-models";
import React, { createContext, useContext, useState, ReactNode } from "react";

const StepperContext = createContext<StepperContextType | undefined>(undefined);

export const useStepper = (): StepperContextType => {
  const context = useContext(StepperContext);
  if (!context) {
    throw new Error("useStepper must be used within a StepperProvider");
  }
  return context;
};

type StepperProviderProps = {
  children: ReactNode;
  steps: Steps;
  isStepValid?: (step: number) => boolean;
  initialStep?: number;
};

const StepperProvider: React.FC<StepperProviderProps> = ({
  children,
  steps,
  isStepValid = () => true,
  initialStep = 1,
}) => {
  const [currentStep, setCurrentStep] = useState(initialStep);
  const [completedSteps, setCompletedSteps] = useState<number[]>(
    Array.from({ length: initialStep - 1 }, (_, i) => i + 1),
  );
  const totalSteps = steps.length;

  const nextStep = () => {
    if (currentStep < totalSteps) {
      setCurrentStep((prevStep) => prevStep + 1);
      if (!completedSteps.includes(currentStep)) {
        setCompletedSteps((prevCompleted) => [...prevCompleted, currentStep]);
      }
    }
  };

  const previousStep = () => {
    if (currentStep > 1) {
      setCurrentStep((prevStep) => prevStep - 1);
      setCompletedSteps((prevCompleted) =>
        prevCompleted.filter((step) => step !== currentStep - 1),
      );
    }
  };

  const goToStep = (step: number) => {
    const canNavigate =
      step > 0 &&
      step <= totalSteps &&
      (step === 1 || completedSteps.includes(step - 1)) &&
      isStepValid(step);

    if (canNavigate) {
      setCurrentStep(step);
      const newCompletedSteps = Array.from({ length: step - 1 }, (_, i) => i + 1);
      setCompletedSteps(newCompletedSteps);
    }
  };

  return (
    <StepperContext.Provider
      value={{
        currentStep,
        nextStep,
        previousStep,
        goToStep,
        completedSteps,
        setCompletedSteps,
        totalSteps,
        getSteps: () => steps,
      }}
    >
      {children}
    </StepperContext.Provider>
  );
};

export default StepperProvider;
