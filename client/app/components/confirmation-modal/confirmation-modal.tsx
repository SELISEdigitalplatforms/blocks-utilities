import React, { ReactNode } from "react";
import {
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogContent,
  DialogFooter,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { Button } from "@/components/ui-kits/button/button";

interface ConfirmationModalProps {
  data: {
    dialogTitle: string;
    dialogSubtitle: ReactNode;
    confirmButton?: string;
    cancelButton?: string;
  };
  onCancel: () => void;
  onConfirm: () => void;
  buttonState?: {
    confirm: { disable: boolean };
  };
}

const ConfirmationModal: React.FC<ConfirmationModalProps> = ({ data, onConfirm, buttonState }) => (
  <DialogContent className="mr-4 w-full max-w-[425px] rounded-md">
    <DialogHeader>
      <DialogTitle className="text-left text-lg font-semibold leading-7">
        {data.dialogTitle}
      </DialogTitle>
      <DialogDescription className="mb-6 mt-2 break-words text-left text-sm font-normal leading-5 text-medium-emphasis">
        {data.dialogSubtitle}
      </DialogDescription>
    </DialogHeader>
    <DialogFooter className="mt-4 flex flex-row gap-2">
      <DialogTrigger asChild>
        <Button variant="outline" size="sm">
          {data.cancelButton || "Cancel"}
        </Button>
      </DialogTrigger>

      <Button size="sm" onClick={onConfirm} disabled={buttonState?.confirm.disable}>
        {data.confirmButton || "Yes"}
      </Button>
    </DialogFooter>
  </DialogContent>
);

export default ConfirmationModal;
