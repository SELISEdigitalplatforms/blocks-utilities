import { useMutation } from "@tanstack/react-query";
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
