import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  IGetMagicUrlByIdPayload,
  IGetMagicUrlsPayload,
  IGetMagicUrlsResponse,
  MagicUrl,
  ICreateMagicUrlPayload,
} from "@blocks-utilities/models/magic-url.model";
import { magicUrlService } from "@blocks-utilities/services/magic-url.service";
import {
  ISaveMagicUrlConfigPayload,
  ISaveMagicUrlConfigResponse,
} from "@blocks-utilities/models/magic-url-config.model";

export const useGetMagicUrls = (option: IGetMagicUrlsPayload) => {
  return useQuery({
    queryKey: ["magic-urls", option.projectKey, option.page, option.pageSize, option.searchText, option.status, option.requestMethod, option.type, option.expiryDateRangeStartDate, option.expiryDateRangeEndDate],
    queryFn: async (): Promise<IGetMagicUrlsResponse> => {
      return await magicUrlService.getMagicUrls(option);
    },
    enabled: !!option.projectKey,
  });
};

export const useGetMagicUrlById = (option: IGetMagicUrlByIdPayload) => {
  return useQuery({
    queryKey: ["magic-url", option.ItemId],
    queryFn: async (): Promise<MagicUrl> => {
      return await magicUrlService.getMagicUrl(option);
    },
    enabled: !!option.ItemId && !!option.projectKey,
  });
};

export const useCreateMagicUrl = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (payload: ICreateMagicUrlPayload) => {
      return await magicUrlService.createMagicUrl(payload);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["magic-urls"] });
    },
  });
};

export const useSaveMagicUrlConfig = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (payload: ISaveMagicUrlConfigPayload) => {
      return await magicUrlService.saveMagicUrlConfig(payload);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["save-magic-url-config"] });
    },
  });
};

export const useGetMagicUrlConfig = (
  projectKey: string,
  options?: { enabled?: boolean },
) => {
  return useQuery<ISaveMagicUrlConfigResponse | null>({
    queryKey: ["magic-url-config", projectKey],
    queryFn: async () => {
      if (!projectKey) return null;
      return await magicUrlService.getMagicUrlConfig(projectKey);
    },
    enabled: !!projectKey && (options?.enabled ?? true),
  });
};

export const useRemoveMagicUrl = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (payload: { linkIds: string[]; projectKey: string }) => {
      return await magicUrlService.deactivateMagicLinks(payload);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["magic-urls"] });
    },
  });
};
