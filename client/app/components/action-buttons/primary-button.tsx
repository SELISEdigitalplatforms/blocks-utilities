import { ElementType, forwardRef, MouseEventHandler } from "react";
import { Button } from "../ui-kits/button/button";
import { CirclePlus } from "lucide-react";

type PrimaryButtonProps = {
  Icon?: ElementType;
  label?: string;
  onClick?: MouseEventHandler;
};

export const PrimaryButton = forwardRef<HTMLButtonElement, PrimaryButtonProps>(
  ({ Icon = CirclePlus, label = "Add", onClick }, ref) => {
    return (
      <Button ref={ref} onClick={onClick}>
        <Icon className="h-5 w-5" />
        <span className="sr-only sm:not-sr-only sm:ml-2.5">{label}</span>
      </Button>
    );
  },
);

PrimaryButton.displayName = "PrimaryButton";
