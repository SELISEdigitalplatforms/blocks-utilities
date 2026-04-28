import { useEffect } from "react";
import { useForm, SubmitHandler } from "react-hook-form";
import { Button } from "@/components/ui-kits/button/button";
import {
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { Input } from "@/components/ui-kits/input/input";
import { showErrorToast, showSuccessToast, useToast } from "@/hooks/use-toast";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { z } from "zod";
import { updateRoleFormSchema } from "./utils";
import { useProjectStore } from "@/store/useProjectStore";
import { IRole } from "@blocks-idp/iam/models/role";
import { useUpdateRole } from "@blocks-idp/iam/hooks/use-roles";
import { isErrorWithErrors } from "@/lib/error";
import { Textarea } from "@/components/ui-kits/textarea/textarea";

type UpdateRoleProps = { role: IRole; isOpen: boolean; onClose: () => void };

export const UpdateRole = ({ role, isOpen, onClose }: UpdateRoleProps) => {
  const { toast } = useToast();
  const { mutateAsync, isPending } = useUpdateRole();
  const tenantId = useProjectStore().selectedProject?.itemId || "";

  const form = useForm({
    defaultValues: role,
    resolver: zodResolver(updateRoleFormSchema),
  });

  const {
    formState: { isDirty },
  } = form;

  const onSubmit: SubmitHandler<z.infer<typeof updateRoleFormSchema>> = async (data) => {
    const newRole = {
      ...data,
      projectKey: tenantId,
      itemId: role.itemId,
    };
    try {
      await mutateAsync(newRole);
      toast({
        variant: "success",
        title: "Success",
        description: "Role updated successfully",
      });
      showSuccessToast({ description: "Role updated successfully" });
      onClose();
    } catch (error) {
      if (isErrorWithErrors(error)) {
        const { errors } = error;
        showErrorToast({ errors });
      }
    }
  };

  useEffect(() => {
    if (!isOpen) form.reset(role);
  }, [role, form, isOpen]);

  return (
    <DialogContent>
      <DialogHeader className="mb-4">
        <DialogTitle>Update Role</DialogTitle>
        <DialogDescription></DialogDescription>
      </DialogHeader>
      <Form {...form}>
        <form onSubmit={form.handleSubmit(onSubmit)} className="flex flex-col gap-4">
          <FormField
            name="name"
            control={form.control}
            render={({ field }) => (
              <FormItem>
                <FormLabel>Name</FormLabel>
                <FormControl>
                  <Input {...field} placeholder="Enter name" />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            name="description"
            control={form.control}
            render={({ field }) => (
              <FormItem>
                <FormLabel>Description</FormLabel>
                <FormControl>
                  <Textarea {...field} placeholder="Enter description" />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <DialogFooter className="mt-6">
            <DialogTrigger asChild>
              <Button className="min-w-[80px]" variant="outline" disabled={isPending}>
                Cancel
              </Button>
            </DialogTrigger>
            <Button className="min-w-[80px]" type="submit" disabled={isPending || !isDirty}>
              {isPending ? "Updating..." : "Update"}
            </Button>
          </DialogFooter>
        </form>
      </Form>
    </DialogContent>
  );
};
