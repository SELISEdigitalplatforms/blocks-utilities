import * as React from "react";
import { StepperContext } from "./context";

function usePrevious<T>(value: T): T | undefined {
	const [state, setState] = React.useState<{ value: T; prev: T | undefined }>({
		value,
		prev: undefined,
	});

	if (state.value !== value) {
		setState({ value, prev: state.value });
	}

	return state.prev;
}

export function useStepper() {
	const context = React.useContext(StepperContext);

	if (context === undefined) {
		throw new Error("useStepper must be used within a StepperProvider");
	}

	const { ...rest } = context;

	const isLastStep = context.activeStep === context.steps.length - 1;
	const hasCompletedAllSteps = context.activeStep === context.steps.length;

	const previousActiveStep = usePrevious(context.activeStep);

	const currentStep = context.steps[context.activeStep];
	const isOptionalStep = !!currentStep?.optional;

	const isDisabledStep = context.activeStep === 0;

	return {
		...rest,
		isLastStep,
		hasCompletedAllSteps,
		isOptionalStep,
		isDisabledStep,
		currentStep,
		previousActiveStep,
	};
}
