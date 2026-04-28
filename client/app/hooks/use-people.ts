import { useProjectStore } from "@/store/useProjectStore";
import { peopleService } from "@blocks-identifier/services/people.service";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

export const useGetPeople = (option: { page: number; pageSize: number; filter: string }) => {
  const projectGroupId = useProjectStore().selectedTenantGroup || "";
  return useQuery({
    queryKey: ["people", option, projectGroupId],
    queryFn: () =>
      peopleService.getPeople({
        ...option,
        projectGroupId,
      }),
    select: (response) => ({
      peoples: response.peoples,
      totalCount: response.totalCount,
      isOwner: response.isOwner,
    }),
    enabled: !!projectGroupId,
    refetchOnMount: "always",
  });
};

export const useInvitePeople = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["people", "add"],
    mutationFn: peopleService.invitePeople,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["people"] });
      queryClient.invalidateQueries({ queryKey: ["subscription-usage"] });
    },
  });
};

export const useResendInvitation = () => {
  return useMutation({
    mutationKey: ["people", "resend-invite"],
    mutationFn: peopleService.resendInvitation,
  });
};

export const useRemoveAccess = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["people", "revoke-access"],
    mutationFn: peopleService.removeAccess,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["people"] });
      queryClient.invalidateQueries({ queryKey: ["subscription-usage"] });
    },
  });
};

export const useRemoveEnvironmentAccess = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["people", "remove-environment-access"],
    mutationFn: peopleService.removeEnvironmentAccess,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["people"] });
      queryClient.invalidateQueries({ queryKey: ["subscription-usage"] });
    },
  });
};

export const useConfirmInvitation = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["people", "confirm-invite"],
    mutationFn: peopleService.confirmInvitation,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["identifier", "projects"] });
    },
  });
};

export const useTransferOwnership = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["people", "transfer-ownership"],
    mutationFn: peopleService.transferOwnership,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["people"] });
      queryClient.invalidateQueries({ queryKey: ["identifier", "projects"] });
    },
  });
};
