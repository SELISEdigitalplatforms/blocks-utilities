import { IGetPublicCertificateResponse } from "@blocks-identifier/models/project.model";
import { projectService } from "@blocks-identifier/services/project.service";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

export const useSavePublicCertificates = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationKey: ["identifier", "public-certificate-url", "save"],
    mutationFn: projectService.savePublicCertificate,
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ["identifier", "public-certificate-url", "get"],
      });
    },
  });
};

export const useGetSavedPublicCertificates = (projectKey: string) => {
  return useQuery<IGetPublicCertificateResponse | null>({
    queryKey: ["identifier", "public-certificate-url", "get", projectKey],
    queryFn: () => projectService.getPublicCertificateInformation(projectKey),
    enabled: !!projectKey,
  });
};

export const useValidateJwksUrl = () => {
  return useMutation({
    mutationKey: ["identifier", "jwks-url", "validate"],
    mutationFn: (url: string) => projectService.validateJwksUrl(url),
  });
};
