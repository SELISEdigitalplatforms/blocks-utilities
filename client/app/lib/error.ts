export const getErrorMessage = (
  error: Record<string, string | string[]>,
  messageMap: Record<string, string> = {},
): string | string[] => {
  if (!error || Object.keys(error).length === 0) {
    return "Something went wrong.";
  }

  const messages: string[] = [];

  for (const key in error) {
    const value = error[key];

    if (messageMap[key]) {
      messages.push(messageMap[key]);
      continue;
    }

    if (typeof value === "string") {
      messages.push(value);
    } else if (Array.isArray(value) && value.length > 0) {
      messages.push(value.join(", "));
    }
  }

  return messages.length ? messages : "Something went wrong.";
};

export const isErrorWithErrors = (
  error: unknown,
): error is { errors: Record<string, string | string[]> } => {
  return (
    typeof error === "object" &&
    error !== null &&
    "errors" in error &&
    typeof (error as { errors: unknown }).errors === "object"
  );
};

export const handleErrorMessages = (
  errors: unknown,
  customMessages?: Record<string, string>,
): string | string[] => {
  if (typeof errors === "string") return errors;

  if (errors && typeof errors === "object" && !Array.isArray(errors)) {
    return getErrorMessage(errors as Record<string, string | string[]>, customMessages);
  }

  return "An unexpected error occurred.";
};
