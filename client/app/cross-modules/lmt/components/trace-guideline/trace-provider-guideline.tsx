import { X } from "lucide-react";
import { motion, AnimatePresence } from "framer-motion";
import { TraceGuideLine } from "./trace-guideline";
import { Separator } from "@/components/ui-kits/separator/separator";
import { TraceGuideSteps } from "./trace-guideline-steps-docs";
import { Button } from "@/components/ui-kits/button/button";
import { TRACE_PROVIDERS } from "../../constants/trace.constant";

type TraceProviderSetupGuideLineProps = {
  provider: TRACE_PROVIDERS;
  open: boolean;
  onOpenChange: (value: boolean) => void;
};

export const TraceProviderSetupGuideLine = ({
  provider,
  open,
  onOpenChange,
}: TraceProviderSetupGuideLineProps) => {
  const steps = TraceGuideSteps[provider] || null;

  if (!steps) return;

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          initial={{ x: "100%" }}
          animate={{ x: 0 }}
          exit={{ x: "100%" }}
          transition={{ duration: 0.3 }}
          className="fixed bottom-1 right-4 flex h-[400px] w-[90vw] flex-col rounded-sm border bg-card shadow-lg sm:right-6 sm:w-[400px]"
        >
          <div>
            <div className="flex items-center justify-between gap-2 p-4">
              <h2 className="text-lg font-semibold">Trace Guide</h2>
              <Button
                variant="ghost"
                onClick={() => onOpenChange(false)}
                className="h-fit w-fit !p-2"
              >
                <X className="aspect-square w-4" />
              </Button>
            </div>
            <Separator orientation="horizontal" />
          </div>
          <TraceGuideLine steps={steps} />
        </motion.div>
      )}
    </AnimatePresence>
  );
};
