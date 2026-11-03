import { useState } from "react";
import { tStatic } from "@seliseblocks/genesis-os";

export const useCopyToClipboard = () => {
  const [isCopying, setIsCopying] = useState(false);

  const copy = async (
    text: string,
    onSuccess?: () => void,
    onError?: (error: Error) => void,
  ): Promise<void> => {
    if (isCopying) return;

    if (!navigator?.clipboard?.writeText) {
      onError?.(new Error(tStatic("utilities.shared", "USE_COPY_TO_CLIPBOARD.CLIPBOARD_API_NOT_SUPPORTED")));
      return;
    }

    try {
      setIsCopying(true);
      await navigator.clipboard.writeText(text);
      onSuccess?.();
    } catch (err) {
      const error = err instanceof Error ? err : new Error(tStatic("utilities.shared", "USE_COPY_TO_CLIPBOARD.UNKNOWN_CLIPBOARD_ERROR"));
      onError?.(error);
    } finally {
      setTimeout(() => setIsCopying(false), 500);
    }
  };

  return { isCopying, copy };
};
