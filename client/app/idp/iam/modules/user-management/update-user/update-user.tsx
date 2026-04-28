import { PrimaryButton } from "@/components/action-buttons/primary-button";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
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
import { isErrorWithErrors } from "@/lib/error";
import { useGetUserById, useUpdateUser } from "@blocks-idp/iam/hooks/use-user";
import { zodResolver } from "@hookform/resolvers/zod";
import { Pen } from "lucide-react";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { inviteUserFormDefaultValue, inviteUserFormSchema } from "./utils";

type UpdateUserProps = {
  id: string;
  projectKey: string;
  own?: boolean;
};

export const UpdateUser = ({ id, projectKey, own = false }: UpdateUserProps) => {
  const [open, setOpen] = useState<boolean>(false);
  const { data, isLoading, isFetching } = useGetUserById({ id, projectKey });
  const { isPending, mutateAsync } = useUpdateUser({ id, projectKey, own });

  const form = useForm({
    defaultValues: inviteUserFormDefaultValue,
    resolver: zodResolver(inviteUserFormSchema),
    values: data?.data,
  });

  const {
    formState: { isDirty },
  } = form;
  const onSubmitHandler = async (values: z.infer<typeof inviteUserFormSchema>) => {
    try {
      const res = await mutateAsync({
        ...data?.data,
        ...values,
        itemId: id,
        projectKey,
      });
      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({ description: "User updated successfully" });
      form.reset();
      setOpen(false);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: "Something went wrong" });
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(value) => {
        form.reset(data?.data || inviteUserFormDefaultValue);
        setOpen(value);
      }}
    >
      <DialogTrigger asChild>
        <PrimaryButton label="Edit User" Icon={Pen} />
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Edit User</DialogTitle>
        </DialogHeader>
        <Form {...form}>
          <form onSubmit={form.handleSubmit(onSubmitHandler)}>
            <div className="flex flex-col gap-4">
              <FormField
                control={form.control}
                name="firstName"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>First name</FormLabel>
                    <FormControl>
                      <Input placeholder="Enter first name" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="lastName"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Last name</FormLabel>
                    <FormControl>
                      <Input placeholder="Enter last name" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </div>
            <DialogFooter className="mt-6">
              <DialogClose asChild>
                <Button variant="secondary" disabled={isPending}>
                  Cancel
                </Button>
              </DialogClose>
              <Button disabled={isPending || isLoading || isFetching || !isDirty}>Save</Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
