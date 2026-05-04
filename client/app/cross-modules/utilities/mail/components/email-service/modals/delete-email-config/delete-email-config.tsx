import { Button } from "@/components/ui-kits/button/button";
import {
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import React from "react";
import { toast } from "@/hooks/use-toast";
import { useProjectStore } from "@/store/useProjectStore";
import { useDeleteEmailConfig } from "../../../../hooks/use-email-config";

interface DeleteEmailConfigProps {
  configId: string;
  onClose: () => void;
}

const DeleteEmailConfig: React.FC<DeleteEmailConfigProps> = ({ configId, onClose }) => {
  const { isPending, mutateAsync } = useDeleteEmailConfig();
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  const deleteConfig = async () => {
    try {
      const res = await mutateAsync({ configurationId: configId, projectKey: tenantId });

      if (res?.isSuccess) {
        toast({
          variant: "success",
          title: "Success",
          description: "Configuration deleted",
        });
        onClose();
      } else {
        toast({
          variant: "destructive",
          title: "Error",
          description: JSON.stringify(res?.errors),
        });
      }
    } catch (error) {
      toast({
        variant: "destructive",
        title: "Error",
        description: JSON.stringify(error),
      });
    }
  };

  return (
    <DialogContent className="rounded-md sm:max-w-[450px]">
      <DialogHeader>
        <DialogTitle className="text-left">Delete configuration</DialogTitle>
        <DialogDescription className="mt-4 text-left">
          <div>Are you sure you’d like to delete this configuration?</div>
          <span className="opacity-0">{configId}</span>
        </DialogDescription>
      </DialogHeader>

      <DialogFooter className="flex flex-row gap-2">
        <DialogTrigger>
          <Button variant="outline" size="default" disabled={false}>
            Cancel
          </Button>
        </DialogTrigger>
        <Button size="default" className="bg-error" onClick={deleteConfig} disabled={isPending}>
          Delete Configuration
        </Button>
      </DialogFooter>
    </DialogContent>
  );
};
export default DeleteEmailConfig;
