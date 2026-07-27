import { useProjectStore } from "@seliseblocks/blocks-kit";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { CreatePaymentRefundCommand } from "../models/payment-refund.model";
import type { PaymentListData } from "../models/payment.model";
import { paymentService } from "../services/payment.service";

export const useCreatePaymentRefund = () => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (command: CreatePaymentRefundCommand) =>
      paymentService.createPaymentRefund(command),
    onSuccess: async (_, command) => {
      queryClient.setQueriesData<PaymentListData>(
        {
          queryKey: ["payments", tenantId],
        },
        (current) =>
          current
            ? {
                ...current,
                items: current.items.map((payment) =>
                  payment.paymentDetailId === command.paymentDetailId
                    ? {
                        ...payment,
                        hasPendingRefund: true,
                      }
                    : payment,
                ),
              }
            : current,
      );

      await queryClient.invalidateQueries({
        queryKey: ["payments", tenantId],
      });
    },
  });
};
