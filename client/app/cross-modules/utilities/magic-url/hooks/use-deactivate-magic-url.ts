import { useRemoveMagicUrl } from "./use-magic-url";
import { toast } from "@/hooks/use-toast";
import { useQueryClient } from "@tanstack/react-query";
import { tStatic } from "@seliseblocks/genesis-os";

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
            title: tStatic("utilities.magic-url", "common:SUCCESS"),
            description: tStatic("utilities.magic-url", "USE_DEACTIVATE_MAGIC_URL.MAGIC_URL_DEACTIVATED_SUCCESSFULLY"),
          });
          queryClient.invalidateQueries({ queryKey: ["magic-url", itemId] });
          onSuccess?.();
        },
        onError: () => {
          toast({
            variant: "destructive",
            title: tStatic("utilities.magic-url", "common:ERROR"),
            description: tStatic("utilities.magic-url", "USE_DEACTIVATE_MAGIC_URL.FAILED_TO_DEACTIVATE_MAGIC_URL"),
          });
        },
      },
    );
  };

  return { deactivateMagicUrl, isRemoving };
};
