import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { toast } from "@/hooks/use-toast";
import { useProjectStore } from "@/store/useProjectStore";
import { useUserPermissions } from "@blocks-idp/iam/hooks/use-user";
import { IPermission } from "@blocks-idp/iam/models/permission";
import { X } from "lucide-react";
import { useState } from "react";

type DeleteUserPermissionProps = {
  permission: IPermission;
  userId: string;
};

export const DeleteUserPermission = ({ permission, userId }: DeleteUserPermissionProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [open, setOpen] = useState<boolean>(false);
  const { deletePermissions, isPending } = useUserPermissions({ userId, projectKey: tenantId });

  const onClickHandler = async () => {
    try {
      const res = await deletePermissions([permission.resource]);
      if (res.isSuccess) {
        toast({
          variant: "success",
          title: "Success",
          description: "Permission is excluded successfully",
        });
        setOpen(false);
      } else {
        toast({
          variant: "destructive",
          title: "Error",
          description: "Something went wrong",
        });
      }
    } catch (error) {
      toast({
        variant: "destructive",
        title: "Error",
        description: `Something went wrong | ${JSON.stringify(error)}`,
      });
    }
  };

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <X className="h-4 w-4" />
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Exclude Permission</DialogTitle>
          <DialogDescription>Are you sure you want to exclude permssion?</DialogDescription>
        </DialogHeader>
        <DialogFooter className="gap-2">
          <DialogClose asChild>
            <Button className="min-w-[80px]" variant="outline" size="default" disabled={isPending}>
              Cancel
            </Button>
          </DialogClose>
          <Button
            className="min-w-[80px]"
            size="default"
            disabled={isPending}
            onClick={onClickHandler}
          >
            {isPending ? "Processing" : "Yes"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
