import { useProjectStore } from "@seliseblocks/blocks-kit";
import { useQuery } from "@tanstack/react-query";
import { paymentService } from "../services/payment.service";

export const usePaymentProviders = () => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  return useQuery({
    queryKey: ["payment-providers", tenantId],
    queryFn: () => paymentService.getPaymentProviders(),
    staleTime: 30_000,
  });
};
