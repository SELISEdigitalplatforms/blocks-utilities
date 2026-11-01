import { useState } from "react";
import { Loader2, RotateCcw } from "lucide-react";
import { v4 as createUuid } from "uuid";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { Input } from "@/components/ui-kits/input/input";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { toast } from "@/hooks/use-toast";
import { useCreatePaymentRefund } from "../hooks/use-create-payment-refund";
import type { PaymentListItem } from "../models/payment.model";
import { useT } from "@seliseblocks/genesis-os";

interface PaymentRefundDialogProps {
  payment: PaymentListItem;
  onClose: () => void;
}

const MAX_REASON_LENGTH = 280;

export const PaymentRefundDialog = ({
  payment,
  onClose,
}: PaymentRefundDialogProps) => {
  const { t } = useT("utilities.payment");
  const { mutateAsync, isPending } = useCreatePaymentRefund();
  const [amount, setAmount] = useState(payment.amount.toString());
  const [reason, setReason] = useState("");
  const [idempotencyKey, setIdempotencyKey] = useState<string>(
    () => createUuid(),
  );
  const [hasAttempted, setHasAttempted] = useState(false);
  const [validationError, setValidationError] = useState<string | null>(
    null,
  );
  const [submissionError, setSubmissionError] = useState<string | null>(
    null,
  );

  const markRequestChanged = () => {
    setValidationError(null);
    setSubmissionError(null);

    if (hasAttempted) {
      setIdempotencyKey(createUuid());
      setHasAttempted(false);
    }
  };

  const submit = async () => {
    const refundAmount = Number(amount);

    if (!Number.isFinite(refundAmount) || refundAmount <= 0) {
      setValidationError(t("PAYMENT_REFUND_DIALOG.ENTER_A_REFUND_AMOUNT_GREATER"));
      return;
    }

    if (refundAmount > payment.amount) {
      setValidationError(
        t("PAYMENT_REFUND_DIALOG.THE_REFUND_AMOUNT_CANNOT_EXCEED", { amount: payment.amount, currencyCode: payment.currencyCode }),
      );
      return;
    }

    const normalizedReason = reason.trim();

    setValidationError(null);
    setSubmissionError(null);
    setHasAttempted(true);

    try {
      const refund = await mutateAsync({
        paymentDetailId: payment.paymentDetailId,
        idempotencyKey,
        request: {
          amount: refundAmount,
          reason: normalizedReason || undefined,
        },
      });

      toast({
        variant: "success",
        title: t("PAYMENT_REFUND_DIALOG.REFUND_REQUEST_SUBMITTED"),
        description: t("PAYMENT_REFUND_DIALOG.REFUND_IS_THE_FINAL_STATUS", { refundId: refund.refundId, status: refund.status.toLowerCase() }),
      });
      onClose();
    } catch (error) {
      setSubmissionError(
        error instanceof Error
          ? error.message
          : t("PAYMENT_REFUND_DIALOG.THE_REFUND_REQUEST_COULD_NOT"),
      );
    }
  };

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !isPending) {
          onClose();
        }
      }}
    >
      <DialogContent hideCloseButton={isPending}>
        <DialogHeader>
          <DialogTitle>{t("PAYMENT_REFUND_DIALOG.REFUND_PAYMENT")}</DialogTitle>
          <DialogDescription>
            {t("PAYMENT_REFUND_DIALOG.ENTER_THE_AMOUNT_TO_RETURN")}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-5 py-1">
          <div className="rounded-lg border bg-muted/30 p-3 text-sm">
            <div className="flex items-center justify-between gap-3">
              <span className="text-muted-foreground">{t("PAYMENT_REFUND_DIALOG.PAYMENT_AMOUNT")}</span>
              <span className="font-semibold tabular-nums">
                {payment.amount} {payment.currencyCode}
              </span>
            </div>
            <p
              className="mt-2 break-all font-mono text-xs text-muted-foreground"
              title={payment.paymentDetailId}
            >
              {payment.paymentDetailId}
            </p>
          </div>

          <div className="space-y-2">
            <label
              htmlFor="payment-refund-amount"
              className="text-sm font-medium"
            >
              {t("PAYMENT_REFUND_DIALOG.REFUND_AMOUNT")}
            </label>
            <div className="relative">
              <Input
                id="payment-refund-amount"
                type="number"
                min="0"
                max={payment.amount}
                step="any"
                inputMode="decimal"
                value={amount}
                className="pr-16"
                disabled={isPending}
                aria-invalid={Boolean(validationError)}
                onChange={(event) => {
                  markRequestChanged();
                  setAmount(event.target.value);
                }}
              />
              <span className="pointer-events-none absolute right-3 top-2.5 text-sm font-medium text-muted-foreground">
                {payment.currencyCode}
              </span>
            </div>
          </div>

          <div className="space-y-2">
            <div className="flex items-center justify-between gap-3">
              <label
                htmlFor="payment-refund-reason"
                className="text-sm font-medium"
              >
                {t("PAYMENT_REFUND_DIALOG.REASON")}
                <span className="ml-1 font-normal text-muted-foreground">
                  {t("PAYMENT_REFUND_DIALOG.OPTIONAL")}
                </span>
              </label>
              <span className="text-xs text-muted-foreground">
                {reason.length}/{MAX_REASON_LENGTH}
              </span>
            </div>
            <Textarea
              id="payment-refund-reason"
              value={reason}
              maxLength={MAX_REASON_LENGTH}
              disabled={isPending}
              placeholder={t("PAYMENT_REFUND_DIALOG.WHY_IS_THIS_PAYMENT_BEING")}
              onChange={(event) => {
                markRequestChanged();
                setReason(event.target.value);
              }}
            />
          </div>

          {(validationError || submissionError) && (
            <div
              role="alert"
              className="rounded-lg border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm text-destructive"
            >
              {validationError || submissionError}
            </div>
          )}
        </div>

        <DialogFooter>
          <Button
            type="button"
            variant="outline"
            disabled={isPending}
            onClick={onClose}
          >
            {t("common:CANCEL")}
          </Button>
          <Button
            type="button"
            variant="destructive"
            disabled={isPending}
            onClick={submit}
          >
            {isPending ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <RotateCcw className="mr-2 h-4 w-4" />
            )}
            {isPending ? t("PAYMENT_REFUND_DIALOG.SUBMITTING_REFUND") : t("PAYMENT_REFUND_DIALOG.CONFIRM_REFUND")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
