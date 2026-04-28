
import { useState } from "react";
import { useForm, SubmitHandler } from "react-hook-form";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { Input } from "@/components/ui-kits/input/input";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { addRoleFormDefaultValue, addRoleFormSchema } from "./utils";
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
import { useProjectStore } from "@/store/useProjectStore";
import { useAddRole } from "@blocks-idp/iam/hooks/use-roles";
import { PrimaryButton } from "@/components/action-buttons/primary-button";
import { Textarea } from "@/components/ui-kits/textarea/textarea";

export const AddRole = () => {
  const [isAddRoleOpenModal, setIsAddRoleOpenModal] = useState(false);
  const { mutateAsync, isPending } = useAddRole();
  const tenantId = useProjectStore().selectedProject?.tenantId || "";

  const form = useForm({
    defaultValues: addRoleFormDefaultValue,
    resolver: zodResolver(addRoleFormSchema),
  });

  const {
    formState: { isDirty },
  } = form;

  const onSubmit: SubmitHandler<z.infer<typeof addRoleFormSchema>> = async (data) => {
    const newRole = {
      name: data.name,
      description: data.description || "",
      slug: data.slug,
      projectKey: tenantId,
    };
    try {
      await mutateAsync(newRole);
      showSuccessToast({ description: "Role added successfully" });
      setIsAddRoleOpenModal(false);
      form.reset();
    } catch (error: unknown) {
      if (error && typeof error === "object" && "status" in error && "errors" in error) {
        const httpError = error as { status: number; errors: Record<string, string | string[]> };

        if (httpError.status === 403) {
          showErrorToast({
            title: "Forbidden",
            errors: "You are not allowed to perform this action.",
          });
        } else {
          showErrorToast({ errors: httpError.errors });
        }
      } else if (error && typeof error === "object" && "errors" in error) {
        showErrorToast({ errors: error.errors });
      }
    }
  };

  return (
    <Dialog
      open={isAddRoleOpenModal}
      onOpenChange={(value) => {
        form.reset(addRoleFormDefaultValue);
        setIsAddRoleOpenModal(value);
      }}
    >
      <DialogTrigger asChild>
        <PrimaryButton label="Add Role" />
      </DialogTrigger>
      <DialogContent>
        <DialogHeader className="mb-4">
          <DialogTitle>Add Role</DialogTitle>
          <DialogDescription>Please fill in the details to add a new role.</DialogDescription>
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
              name="slug"
              control={form.control}
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Slug</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="Enter slug" />
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
                {isPending ? "Adding..." : "Add"}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
