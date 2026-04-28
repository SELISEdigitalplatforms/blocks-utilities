

import { Button } from "@/components/ui-kits/button/button";
import {
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Input } from "@/components/ui-kits/input/input";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useProjectStore } from "@/store/useProjectStore";
import { useSaveOrganization } from "@blocks-idp/iam/hooks/use-organization";
import { IOrganization } from "@blocks-idp/iam/models/organization";
import { zodResolver } from "@hookform/resolvers/zod";
import { useEffect } from "react";
import { SubmitHandler, useForm } from "react-hook-form";
import { z } from "zod";
import { updateOrganizationFormSchema } from "./utils";

type UpdateOrganizationProps = {
  organization: IOrganization;
  isOpen: boolean;
};

export const UpdateOrganization = ({ organization, isOpen }: UpdateOrganizationProps) => {
  const { mutateAsync, isPending } = useSaveOrganization();
  const tenantId = useProjectStore().selectedProject?.tenantId || "";

  const form = useForm({
    defaultValues: { name: organization.name },
    resolver: zodResolver(updateOrganizationFormSchema),
  });

  const {
    formState: { isDirty },
  } = form;

  const onSubmit: SubmitHandler<z.infer<typeof updateOrganizationFormSchema>> = async (data) => {
    try {
      const res = await mutateAsync({
        projectKey: tenantId,
        name: data.name,
        itemId: organization.itemId,
        isEnable: organization.isEnable,
      });
      if (!res.isSuccess) {
        showErrorToast({ errors: res.errors });
        return;
      }
      showSuccessToast({ description: "Organization renamed successfully" });
    } catch (error: unknown) {
      if (error && typeof error === "object" && "errors" in error) {
        showErrorToast({ errors: error.errors });
      }
    }
  };

  useEffect(() => {
    if (!isOpen) {
      form.reset({ name: organization.name });
    }
  }, [isOpen, organization.name, form]);

  return (
    <DialogContent>
      <DialogHeader className="mb-4">
        <DialogTitle>Rename Organization</DialogTitle>
        <DialogDescription>Update the organization name</DialogDescription>
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
                  <Input {...field} placeholder="Enter organization name" />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <DialogFooter className="mt-6">
            <DialogClose asChild>
              <Button className="min-w-[80px]" variant="outline" disabled={isPending}>
                Cancel
              </Button>
            </DialogClose>
            <Button className="min-w-[80px]" type="submit" disabled={isPending || !isDirty}>
              {isPending ? "Saving..." : "Save"}
            </Button>
          </DialogFooter>
        </form>
      </Form>
    </DialogContent>
  );
};
