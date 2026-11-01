import { Button } from "@/components/ui-kits/button/button";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui-kits/popover/popover";
import { Shield, ShieldCheck } from "lucide-react";
import { useT } from "@seliseblocks/genesis-os";

type SecurityPresetsPopoverProps = {
  onEnableAllMfa: () => void;
  onEnableAllCaptcha: () => void;
};

export const SecurityPresetsPopover = ({
  onEnableAllMfa,
  onEnableAllCaptcha,
}: SecurityPresetsPopoverProps) => {
  const { t } = useT("utilities.idp");
  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button
          variant="outline"
          size="sm"
          className="gap-1.5 px-2 sm:px-3 font-medium"
        >
          <Shield className="h-3.5 w-3.5 shrink-0" />
          <span className="hidden lg:inline">{t("SECURITY_PRESETS_POPOVER.SECURITY_PRESETS")}</span>
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-52 p-1.5">
        <button
          className="flex w-full items-center gap-2.5 rounded-md px-3 py-2 text-left text-sm text-foreground hover:bg-accent hover:text-accent-foreground"
          onClick={onEnableAllMfa}
        >
          <ShieldCheck className="h-4 w-4 shrink-0 text-amber-500" />
          {t("SECURITY_PRESETS_POPOVER.ENABLE_ALL_MFA")}
        </button>
        <button
          className="flex w-full items-center gap-2.5 rounded-md px-3 py-2 text-left text-sm text-foreground hover:bg-accent hover:text-accent-foreground"
          onClick={onEnableAllCaptcha}
        >
          <Shield className="h-4 w-4 shrink-0 text-blue-500" />
          {t("SECURITY_PRESETS_POPOVER.ENABLE_ALL_CAPTCHA")}
        </button>
      </PopoverContent>
    </Popover>
  );
};
