import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { RotatePaymentProviderCredentialsCommand } from "../models/payment-provider.model";
import { paymentService } from "../services/payment.service";

export const useRotatePaymentProviderCredentials = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (
      command: RotatePaymentProviderCredentialsCommand,
    ) => paymentService.rotatePaymentProviderCredentials(command),
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: ["payment-providers"],
      }),
  });
};
