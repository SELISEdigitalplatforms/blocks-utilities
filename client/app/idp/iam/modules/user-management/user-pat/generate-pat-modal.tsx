import React, { useState } from "react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { IGeneratePATPayload, IPATResponse } from "@/idp/iam/models/user";
import { useGeneratePats } from "@/idp/iam/hooks/use-activity";
import { useT } from "@seliseblocks/genesis-os";

interface GenerateTokenModalProps {
  isOpen: boolean;
  onClose: () => void;
  id: string;
  onSuccess?: (data: IPATResponse) => void;
}

export function GenerateTokenModal({ isOpen, onClose, onSuccess }: GenerateTokenModalProps) {
  const { t } = useT("utilities.idp");
  const [note, setNote] = useState("");
  const [expiration, setExpiration] = useState("30");

  const { mutate: generateToken, isPending, isError } = useGeneratePats();

  const getExpirationDate = (days: number): string => {
    const date = new Date();
    date.setDate(date.getDate() + days);

    return date.toLocaleDateString("en-GB", {
      day: "2-digit",
      month: "2-digit",
      year: "numeric",
    });
  };


  const getExpirationLabel = (days: string): string => {
    const daysNum = parseInt(days);
    return t("GENERATE_PAT_MODAL.EXPIRATION_LABEL", { days, date: getExpirationDate(daysNum) });
  };

  const handleGenerate = () => {
    if (!note.trim()) {
      console.error("Name is required");
      return;
    }

    const expirationDays = parseInt(expiration);

    let clientIdEnvWise;
    if (import.meta.env.BLOCKS_APP_URL === "https://dev-cloud.seliseblocks.com") {
      clientIdEnvWise = "11640778-423d-41e6-acba-1cf947cecb54";
    } else if (import.meta.env.BLOCKS_APP_URL === "https://stg-cloud.seliseblocks.com") {
      clientIdEnvWise = "4fe41cda-cb8d-458e-8a95-010549bd6d7e";
    } else if (import.meta.env.BLOCKS_APP_URL === "https://cloud.seliseblocks.com") {
      clientIdEnvWise = "dce12fb6-3ed7-4704-9426-81d7d957dfb8";
    } else {
      clientIdEnvWise = "11640778-423d-41e6-acba-1cf947cecb54";
    }

    const payload: IGeneratePATPayload = {
      clientId: clientIdEnvWise,
      note: note || undefined,
      codeTtlInMinute: expirationDays * 24 * 60,
    };

    generateToken(payload, {
      onSuccess: (data) => {
        setNote("");
        setExpiration("30");

        onSuccess?.(data);

        onClose();
      },
      onError: (error) => {
        console.error("Failed to generate token:", error);
      },
    });
  };

  const handleCancel = () => {
    onClose();
    setNote("");
    setExpiration("30");
  };

  const handleOpenChange = (open: boolean) => {
    if (!open) {
      handleCancel();
    }
  };

  return (
    <Dialog open={isOpen} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="text-xl font-semibold">{t("GENERATE_PAT_MODAL.GENERATE_TOKEN")}</DialogTitle>
          <DialogDescription className="text-sm text-gray-600">
            {t("GENERATE_PAT_MODAL.CREATE_A_SECURE_ACCESS_TOKEN")}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4 py-4">
          {isError && (
            <div className="rounded border border-red-200 bg-red-50 px-4 py-3 text-red-700">
              <p className="text-sm">{t("GENERATE_PAT_MODAL.FAILED_TO_GENERATE_TOKEN_PLEASE")}</p>
            </div>
          )}

          <div className="space-y-2">
            <Label htmlFor="note" className="text-sm font-medium">
              {t("GENERATE_PAT_MODAL.PAT_NAME", { span: "*" })}
            </Label>
            <Input
              id="note"
              placeholder={t("GENERATE_PAT_MODAL.WRITE_HERE")}
              value={note}
              onChange={(e) => setNote(e.target.value)}
              className="w-full"
              disabled={isPending}
              required
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="expiration" className="text-sm font-medium">
              {t("GENERATE_PAT_MODAL.EXPIRATION")}
            </Label>
            <Select value={expiration} onValueChange={setExpiration} disabled={isPending}>
              <SelectTrigger className="w-full">
                <SelectValue>{getExpirationLabel(expiration)}</SelectValue>
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="30">{t("GENERATE_PAT_MODAL.N_30_DAYS", { date: getExpirationDate(30) })}</SelectItem>
                <SelectItem value="15">{t("GENERATE_PAT_MODAL.N_15_DAYS", { date: getExpirationDate(15) })}</SelectItem>
                <SelectItem value="7">{t("GENERATE_PAT_MODAL.N_7_DAYS", { date: getExpirationDate(7) })}</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>

        <DialogFooter className="flex gap-3 sm:gap-3">
          <Button
            variant="outline"
            onClick={handleCancel}
            className="flex-1 sm:flex-none"
            disabled={isPending}
          >
            {t("common:CANCEL")}
          </Button>
          <Button
            onClick={handleGenerate}
            className="flex-1 sm:flex-none"
            disabled={isPending || !note.trim()}
          >
            {isPending ? t("GENERATE_PAT_MODAL.GENERATING") : t("GENERATE_PAT_MODAL.GENERATE")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
