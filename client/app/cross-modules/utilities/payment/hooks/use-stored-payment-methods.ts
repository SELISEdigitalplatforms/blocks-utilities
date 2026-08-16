import { useProjectStore } from "@seliseblocks/genesis-os";
import {
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { paymentService } from "../services/payment.service";

const STORED_PAYMENT_METHOD_QUERY_KEY = "stored-payment-methods";

export const useStoredPaymentMethods = (organizationId?: string) => {
  const tenantId =
    useProjectStore()?.selectedProject?.tenantId || "";

  return useQuery({
    // The organization is part of the key: without it, switching organizations shows the
    // previous one's cards from cache until the stale time expires.
    queryKey: [
      STORED_PAYMENT_METHOD_QUERY_KEY,
      tenantId,
      organizationId ?? "",
    ],
    queryFn: () => paymentService.getStoredPaymentMethods(organizationId),
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
