import { X } from "lucide-react";
import { motion, AnimatePresence } from "framer-motion";
import { SSOSetupGuideLine } from "./sso-setup-guideline";
import { Separator } from "@/components/ui-kits/separator/separator";
import { SSOSetupGuideSteps } from "./sso-setup-guideline-steps-docs";
import { Button } from "@/components/ui-kits/button/button";
import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";

type SSoProviderSetupGuideLineProps = {
  provider: SSO_PROVIDERS;
  open: boolean;
  onOpenChange: (value: boolean) => void;
};

export const SSoProviderSetupGuideLine = ({ provider, open, onOpenChange }: SSoProviderSetupGuideLineProps) => {
  const steps = SSOSetupGuideSteps[provider] || null;

  if (!steps) return;

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          initial={{ x: "100%" }}
          animate={{ x: 0 }}
          exit={{ x: "100%" }}
          transition={{ duration: 0.3 }}
          className="fixed bottom-0 left-0 right-0 top-0 flex h-screen w-full flex-col border bg-card shadow-lg md:bottom-1 md:left-auto md:right-6 md:top-auto md:h-[400px] md:w-[400px] md:rounded-sm"
        >
          <div>
            <div className="flex items-center justify-between gap-2 p-4">
              <h2 className="text-lg font-semibold">Setup Guide</h2>
              <Button variant="ghost" onClick={() => onOpenChange(false)} className="h-fit w-fit !p-2">
                <X className="aspect-square w-4" />
              </Button>
            </div>
            <Separator orientation="horizontal" />
          </div>
          <SSOSetupGuideLine steps={steps} />
        </motion.div>
      )}
    </AnimatePresence>
  );
};
