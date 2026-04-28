import { useRemoveMagicUrl } from "./use-magic-url";
import { toast } from "@/hooks/use-toast";
import { useQueryClient } from "@tanstack/react-query";

export const useDeactivateMagicUrl = () => {
  const queryClient = useQueryClient();
  const { mutate: removeMagicUrl, isPending: isRemoving } = useRemoveMagicUrl();

  const deactivateMagicUrl = (
    itemId: string,
    projectKey: string,
    onSuccess?: () => void,
  ) => {
    removeMagicUrl(
      { linkIds: [itemId], projectKey },
      {
        onSuccess: () => {
          toast({
            variant: "success",
            title: "Success",
            description: "Magic URL deactivated successfully",
          });
          queryClient.invalidateQueries({ queryKey: ["magic-url", itemId] });
          onSuccess?.();
        },
        onError: () => {
          toast({
            variant: "destructive",
            title: "Error",
            description: "Failed to deactivate Magic URL",
          });
        },
      },
    );
  };

  return { deactivateMagicUrl, isRemoving };
};
