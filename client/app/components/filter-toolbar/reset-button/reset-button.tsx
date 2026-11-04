import { Button } from "@/components/ui-kits/button/button";
import { X } from "lucide-react";
import { MouseEventHandler } from "react";
import { useT } from "@seliseblocks/genesis-os";

type ResetButtonProps = {
  onClick: MouseEventHandler<HTMLButtonElement>;
};

export const ResetButton = ({ onClick }: ResetButtonProps) => {
  const { t } = useT("utilities.shared");
  return (
    <Button type="button" variant="outline" onClick={onClick} className="h-8 px-2 lg:px-3">
      {t("common:RESET")}
      <X className="ml-2 h-4 w-4" />
    </Button>
  );
};
