import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { notificationService } from "../services/notification.service";
import { useProjectStore } from "@/store/useProjectStore";

export const useGetNotificationConfigs = (page: number = 0, pageSize: number = 10) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  return useQuery({
    queryKey: ["notificationConfigs", page, pageSize, tenantId],
    queryFn: () => notificationService.getNotificationConfigs(page, pageSize, tenantId),
    enabled: !!tenantId,
  });
};

export const useSaveNotificationConfig = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["saveNotificationConfig"],
    mutationFn: notificationService.saveNotificationConfig,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["notificationConfigs"] });
    },
  });
};

export const useDeleteNotificationConfig = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["deleteNotificationConfig"],
    mutationFn: notificationService.deleteNotificationConfig,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["notificationConfigs"] });
    },
  });
};
