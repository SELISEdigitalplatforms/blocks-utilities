import { useProjectStore } from "@/store/useProjectStore";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { emailService } from "@blocks-communication/mail/services/email.services";

export const useGetEmailConfigs = (pageNumber: number, pageSize: number) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  return useQuery({
    queryKey: ["email-configs", tenantId, pageNumber, pageSize],
    queryFn: () => emailService.fetchEmailConfigs(tenantId, pageNumber, pageSize),
  });
};

export const useSaveEmailConfig = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["email-config", "add"],
    mutationFn: emailService.saveMailConfig,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["email-configs"] });
    },
  });
};

export const useDeleteEmailConfig = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["email-config", "delete"],
    mutationFn: emailService.deleteMailConfig,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["email-configs"] });
    },
  });
};
