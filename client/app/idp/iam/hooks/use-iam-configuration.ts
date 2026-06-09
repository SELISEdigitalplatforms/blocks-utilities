import { useMutation, useQuery } from "@tanstack/react-query";
import { useProjectStore } from "@seliseblocks/blocks-kit";

import { configurationService } from "@blocks-idp/iam/services/configuration.service";

export const useGetIamConfiguration = () => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  return useQuery({
    queryKey: ["iam", "configuration", "get", tenantId],
    queryFn: () => configurationService.getIamConfiguration(tenantId),
  });
};

export const useSaveIamConfiguration = () => {
  return useMutation({
    mutationKey: ["iam", "configuration", "save"],
    mutationFn: configurationService.saveIamConfiguration,
  });
};
