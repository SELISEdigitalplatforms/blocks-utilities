import { useProjectStore } from "@seliseblocks/blocks-kit";
import {
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { paymentService } from "../services/payment.service";

const STORED_PAYMENT_METHOD_QUERY_KEY = "stored-payment-methods";

export const useStoredPaymentMethods = () => {
  const tenantId =
    useProjectStore()?.selectedProject?.tenantId || "";

  return useQuery({
    queryKey: [STORED_PAYMENT_METHOD_QUERY_KEY, tenantId],
    queryFn: () => paymentService.getStoredPaymentMethods(),
    staleTime: 15_000,
  });
};

export const useRemoveStoredPaymentMethod = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (paymentMethodId: string) =>
      paymentService.removeStoredPaymentMethod(paymentMethodId),
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: [STORED_PAYMENT_METHOD_QUERY_KEY],
      }),
  });
};
