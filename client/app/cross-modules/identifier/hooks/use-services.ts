import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { serviceRegistryService } from "@blocks-identifier/services/service-registery.service";
import { IGetAllServicesPayload, IRegisterServicePayload } from "@blocks-identifier/types/services.type";

export const useRegisterService = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["service", "register"],
    mutationFn: (payload: IRegisterServicePayload) => serviceRegistryService.registerService(payload),
    onSuccess: (res) => {
      if (res.isSuccess) queryClient.invalidateQueries({ queryKey: ["services"] });
    },
  });
};

export const useGetAllServices = (options: IGetAllServicesPayload) => {
  return useQuery({
    queryKey: ["services", options.projectKey, options.page, options.pageSize],
    queryFn: () => serviceRegistryService.getAllServices(options),
    enabled: !!options.projectKey,
  });
};
