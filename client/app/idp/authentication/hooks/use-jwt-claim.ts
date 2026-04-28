import { GetJwtClaimPayload } from "@blocks-idp/authentication/models/jwt.claim.model";
import { projectService } from "@blocks-identifier/services/project.service";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

export const useAddJwtClaim = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["assets", "add"],
    mutationFn: projectService.addJwtClaim,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["get-assets"] });
      queryClient.invalidateQueries({ queryKey: ["env-repositories"] });
      queryClient.invalidateQueries({ queryKey: ["get-jwt-claim"] });
    },
  });
};

export const useGetJwtClaim = (payload: GetJwtClaimPayload, enabled: boolean = true) => {
  return useQuery({
    queryKey: ["get-jwt-claim", payload.projectKey],
    queryFn: () => projectService.getJwtClaim(payload),
    enabled: !!payload.projectKey && enabled,
  });
};
