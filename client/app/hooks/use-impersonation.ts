import { useMutation } from "@tanstack/react-query";
import { impersonationService } from "@/services/impersonation.service";
import { debug } from "@/lib/debug";

export const useStartImpersonation = () => {
  return useMutation({
    mutationKey: ["impersonation", "start"],
    mutationFn: async (request: Parameters<typeof impersonationService.startImpersonation>[0]) => {
      debug.group("[useStartImpersonation] mutationFn");
      debug.log("Calling impersonationService.startImpersonation:", request);
      try {
        const result = await impersonationService.startImpersonation(request);
        debug.log("Impersonation result:", result);
        debug.groupEnd();
        return result;
      } catch (err) {
        debug.error("Impersonation error:", err);
        debug.groupEnd();
        throw err;
      }
    },
  });
};

export const useStopImpersonation = () => {
  return useMutation({
    mutationKey: ["impersonation", "stop"],
    mutationFn: async () => {
      debug.group("[useStopImpersonation] mutationFn");
      try {
        await impersonationService.stopImpersonation();
        debug.log("Stop impersonation success");
        debug.groupEnd();
      } catch (err) {
        debug.error("Stop impersonation error:", err);
        debug.groupEnd();
        throw err;
      }
    },
  });
};
