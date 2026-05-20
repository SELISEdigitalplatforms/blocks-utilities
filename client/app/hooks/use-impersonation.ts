import { useMutation, useQuery } from "@tanstack/react-query";
import { impersonationService } from "@/services/impersonation.service";

export const useStartImpersonation = () => {
  return useMutation({
    mutationKey: ["impersonation", "start"],
    mutationFn: impersonationService.startImpersonation,
  });
};

export const useStopImpersonation = () => {
  return useMutation({
    mutationKey: ["impersonation", "stop"],
    mutationFn: impersonationService.stopImpersonation,
  });
};

export const useImpersonationStatusChecker = () => {
  return useQuery({
    queryKey: ["impersonation", "status"],
    queryFn: () => impersonationService.impersonationStatus(),
  });
};
