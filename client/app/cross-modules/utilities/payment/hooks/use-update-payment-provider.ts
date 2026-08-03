import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { UpdatePaymentProviderCommand } from "../models/payment-provider.model";
import { paymentService } from "../services/payment.service";

export const useUpdatePaymentProvider = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (command: UpdatePaymentProviderCommand) =>
      paymentService.updatePaymentProvider(command),
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: ["payment-providers"],
      }),
  });
};
