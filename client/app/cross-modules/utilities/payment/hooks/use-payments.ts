import { useProjectStore } from "@seliseblocks/genesis-os";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { PAYMENT_LIST_REFRESH_INTERVAL_MS } from "../constants/payment.constants";
import type { PaymentQuery } from "../models/payment.model";
import { paymentService } from "../services/payment.service";

export const usePayments = (query: PaymentQuery) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  return useQuery({
    queryKey: ["payments", tenantId, query],
    queryFn: () => paymentService.getPayments(query),
    placeholderData: keepPreviousData,
    refetchInterval: PAYMENT_LIST_REFRESH_INTERVAL_MS,
    staleTime: 5_000,
  });
};
