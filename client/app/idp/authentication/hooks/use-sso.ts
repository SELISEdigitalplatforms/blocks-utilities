import {
  IGetSsoCredentialByIdPayload,
  IGetSsoCredentialsPayload,
} from "@blocks-idp/authentication/models/sso.model";
import { ssoService } from "@blocks-idp/authentication/services/social.service";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

export const useGetSsoCredentials = (option: IGetSsoCredentialsPayload) => {
  return useQuery({
    queryKey: ["sso", option],
    queryFn: () => ssoService.getSsoCredentials(option),
  });
};

export const useGetSsoCredentialById = (option: IGetSsoCredentialByIdPayload) => {
  return useQuery({
    queryKey: ["sso", option],
    queryFn: () => ssoService.getSsoCredentialId(option),
    enabled: !!option.itemId,
  });
};

export const useSaveSsoCredential = () => {
  const quertClient = useQueryClient();
  return useMutation({
    mutationKey: ["sso", "save"],
    mutationFn: ssoService.saveSsoCredential,
    onSuccess: () => {
      quertClient.invalidateQueries({ queryKey: ["sso"] });
    },
  });
};

export const useDeleteSsoCredential = () => {
  const quertClient = useQueryClient();
  return useMutation({
    mutationKey: ["sso", "delete"],
    mutationFn: ssoService.deleteSsoCredential,
    onSuccess: () => {
      quertClient.invalidateQueries({ queryKey: ["sso"] });
    },
  });
};

export const useUpdateSsoCredentialStatus = () => {
  const quertClient = useQueryClient();
  return useMutation({
    mutationKey: ["sso", "udpate"],
    mutationFn: ssoService.updateSsoCredentialStatus,
    onSuccess: () => {
      quertClient.invalidateQueries({ queryKey: ["sso"] });
    },
  });
};

export const useSaveOIDCCredential = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["sso", "save", "oidc"],
    mutationFn: ssoService.saveBlocksSsoCredential,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["oidc"] });
    }
  })
}

export const useSaveGetOIDCCredential = (projectKey: string) => {
  return useQuery({
    queryKey: ["sso", projectKey],
    queryFn: () => ssoService.getBlocksSsoCredential(projectKey),
    enabled: !!projectKey,
  });
}
