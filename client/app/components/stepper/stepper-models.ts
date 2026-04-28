export type Steps = Array<{ id: number; title: string }>;

export type StepperContextType = {
  currentStep: number;
  nextStep: () => void;
  previousStep: () => void;
  goToStep: (step: number) => void;
  setCompletedSteps: (steps: number[]) => void;
  completedSteps: number[];
  totalSteps: number;
  getSteps: () => Steps;
};
