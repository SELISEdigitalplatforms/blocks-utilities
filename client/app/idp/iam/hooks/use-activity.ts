import { toast } from "@/hooks/use-toast";
import { IGeneratePATPayload, IGetHistoriesPayload, IGetSessionPayload } from "@blocks-idp/iam/models/user";
import { userService } from "@blocks-idp/iam/services/user.service";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { tStatic } from "@seliseblocks/genesis-os";

export const useGetSessions = (option: IGetSessionPayload) => {
  return useQuery({
    queryKey: ["sessions", option],
    queryFn: () => userService.getSessions(option),
  });
};

export const useGetHistories = (option: IGetHistoriesPayload) => {
  return useQuery({
    queryKey: ["histories", option],
    queryFn: () => userService.getHistories(option),
  });
};

export const useGetPats = () => {
  return useQuery({
    queryKey: ["personalAccessTokens"],
    queryFn: () => userService.getPats(),
    select: (data) => {
      if (!data || !Array.isArray(data)) return [];

      return [...data].sort((a, b) => {
        const dateA = new Date(a.createdDate || 0).getTime();
        const dateB = new Date(b.createdDate || 0).getTime();
        return dateB - dateA;
      });
    }
  });
};

export const useGeneratePats = () => {
    const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: IGeneratePATPayload) => userService.generatePats(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["personalAccessTokens"] });
      toast({ variant: "success", description: tStatic("utilities.idp", "USE_ACTIVITY.TOKEN_GENERATED_SUCCESSFULLY") });
    },
    onError: () => {
      toast({ variant: "destructive", description: tStatic("utilities.idp", "USE_ACTIVITY.FAILED_TO_GENERATE_TOKEN") });
    },
  });
};
