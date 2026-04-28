import { useState } from "react";

export const useCopyToClipboard = () => {
  const [isCopying, setIsCopying] = useState(false);

  const copy = async (
    text: string,
    onSuccess?: () => void,
    onError?: (error: Error) => void,
  ): Promise<void> => {
    if (isCopying) return;

    if (!navigator?.clipboard?.writeText) {
      onError?.(new Error("Clipboard API not supported"));
      return;
    }

    try {
      setIsCopying(true);
      await navigator.clipboard.writeText(text);
      onSuccess?.();
    } catch (err) {
      const error = err instanceof Error ? err : new Error("Unknown clipboard error");
      onError?.(error);
    } finally {
      setTimeout(() => setIsCopying(false), 500);
    }
  };

  return { isCopying, copy };
};
