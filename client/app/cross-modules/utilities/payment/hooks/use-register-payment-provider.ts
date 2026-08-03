import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { RegisterPaymentProviderRequest } from "../models/payment-provider.model";
import { paymentService } from "../services/payment.service";

export const useRegisterPaymentProvider = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: RegisterPaymentProviderRequest) =>
      paymentService.registerPaymentProvider(request),
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: ["payment-providers"],
      }),
  });
};
