import { useQuery } from "@tanstack/react-query";
import { userService } from "@blocks-idp/iam/services/user.service";
import { useAuthStore } from "@seliseblocks/genesis-os";

export const useGetCreator = (createdBy: string | undefined | null, tenantId: string) => {
  const { user } = useAuthStore();
  const isCurrentUser = user?.sub === createdBy;

  return useQuery({
    queryKey: ["user", createdBy, isCurrentUser ? "current" : tenantId],
    queryFn: () => {
      if (isCurrentUser) {
        return userService.getUser();
      }
      return userService.getUserById({ id: createdBy || "", projectKey: tenantId });
    },
    enabled: !!createdBy,
  });
};
