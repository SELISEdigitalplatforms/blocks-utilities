import { CaptchaProps, CaptchaRef } from "@/components/captcha/index.type";
import { useTheme } from "@/hooks/use-theme";
import { useCallback, useRef, useState } from "react";

type UseCaptchaProps = {
  siteKey: string;
  type: CaptchaProps["type"];
};

type UseCaptchaReturn = {
  code: string;
  reset: () => void;
  ref: React.RefObject<CaptchaRef>;
  captcha: {
    ref: React.RefObject<CaptchaRef>;
    type: CaptchaProps["type"];
    siteKey: string;
    theme: "dark" | "light";
    onVerify: (code: string) => void;
    onExpired: () => void;
    onError: () => void;
  };
};

export const useCaptcha = ({ siteKey, type }: UseCaptchaProps): UseCaptchaReturn => {
  const [code, setCode] = useState("");
  const { theme } = useTheme();
  const ref = useRef<CaptchaRef>(null);

  const reset = useCallback(() => {
    ref.current?.reset();
    setCode("");
  }, []);

  return {
    code,
    reset,
    ref,
    captcha: {
      ref,
      type,
      siteKey,
      theme: theme === "dark" ? "dark" : "light",
      onVerify: setCode,
      onExpired: () => setCode(""),
      onError: () => setCode(""),
    },
  };
};
