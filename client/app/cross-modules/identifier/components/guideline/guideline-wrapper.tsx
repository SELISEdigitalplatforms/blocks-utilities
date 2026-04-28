import { X } from "lucide-react";
import { motion, AnimatePresence } from "framer-motion";
import { GuideLine } from "./guideline";
import { Separator } from "@/components/ui-kits/separator/separator";
import { Button } from "@/components/ui-kits/button/button";

type Step = {
  id: string;
  description: React.ReactNode;
};

type GuideLineWrapperProps = {
  title: string;
  content: Step[];
  open: boolean;
  onOpenChange: (value: boolean) => void;
};

export const GuideLineWrapper = ({ title, content, open, onOpenChange }: GuideLineWrapperProps) => {
  if (!content) return null;

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
            <div className="flex items-center justify-between gap-2 p-4 py-2">
              <h2 className="text-lg font-semibold">{title}</h2>
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
          <GuideLine steps={content} />
        </motion.div>
      )}
    </AnimatePresence>
  );
};
