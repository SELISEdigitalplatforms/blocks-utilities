import { useProjectStore } from "@seliseblocks/genesis-os";
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
