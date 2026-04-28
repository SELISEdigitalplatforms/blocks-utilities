import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { storageService } from "../services/storage.service";
import { useProjectStore } from "@/store/useProjectStore";

export const useGetStorageConfigurations = () => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  return useQuery({
    queryKey: ["storage", "configuration", "gets", tenantId],
    queryFn: () => storageService.configuration.gets(tenantId),
  });
};

export const useSaveStorageConfiguration = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["storage", "configuration", "save"],
    mutationFn: storageService.configuration.save,
    onSuccess: (data) => {
      if (data.isSuccess)
        queryClient.invalidateQueries({ queryKey: ["storage", "configuration", "gets"] });
    },
  });
};
export const useDeleteStorageConfiguration = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["storage", "configuration", "delete"],
    mutationFn: storageService.configuration.delete,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["storage", "configuration", "gets"] });
    },
  });
};
