import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { CreatePaymentCommand } from "../models/create-payment.model";
import { paymentService } from "../services/payment.service";

export const useCreatePayment = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (command: CreatePaymentCommand) =>
      paymentService.createPayment(command),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["payments"] });
    },
  });
};
