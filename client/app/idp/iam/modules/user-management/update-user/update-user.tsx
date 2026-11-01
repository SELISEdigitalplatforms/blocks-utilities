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
import { useGetMe, useUpdateUser } from "@/idp/iam/hooks/use-user";
import { zodResolver } from "@hookform/resolvers/zod";
import { Pen } from "lucide-react";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { inviteUserFormDefaultValue, createInviteUserFormSchema } from "./utils";
import { useT } from "@seliseblocks/genesis-os";
type UpdateUserProps = {
  id: string;
  projectKey: string;
  own?: boolean;
};
export const UpdateUser = ({ id, projectKey, own = false }: UpdateUserProps) => {
  const { t } = useT("utilities.idp");
  const [open, setOpen] = useState<boolean>(false);
  const { data, isLoading, isFetching } = useGetMe();
  const { isPending, mutateAsync } = useUpdateUser({ id, projectKey, own });
  const form = useForm({
    defaultValues: inviteUserFormDefaultValue,
    resolver: zodResolver(createInviteUserFormSchema()),
    values: data?.data,
  });
  const {
    formState: { isDirty },
  } = form;
  const onSubmitHandler = async (values: z.infer<ReturnType<typeof createInviteUserFormSchema>>) => {
    try {
      const res = await mutateAsync({
        ...data?.data,
        ...values,
        itemId: id,
        projectKey,
      });
      if (!res.isSuccess) {
        showErrorToast({ errors: res.errors });
        return;
      }
      showSuccessToast({ description: t("UPDATE_USER.USER_UPDATED_SUCCESSFULLY") });
      form.reset();
      setOpen(false);
    } catch (error) {
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors });
        return;
      }
      showErrorToast({ errors: t("UPDATE_USER.SOMETHING_WENT_WRONG") });
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
        <PrimaryButton label={t("UPDATE_USER.EDIT_USER")} Icon={Pen} />
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("UPDATE_USER.EDIT_USER")}</DialogTitle>
        </DialogHeader>
        <Form {...form}>
          <form onSubmit={form.handleSubmit(onSubmitHandler)}>
            <div className="flex flex-col gap-4">
              <FormField
                control={form.control}
                name="firstName"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("UPDATE_USER.FIRST_NAME")}</FormLabel>
                    <FormControl>
                      <Input placeholder={t("UPDATE_USER.ENTER_FIRST_NAME")} {...field} />
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
                    <FormLabel>{t("UPDATE_USER.LAST_NAME")}</FormLabel>
                    <FormControl>
                      <Input placeholder={t("UPDATE_USER.ENTER_LAST_NAME")} {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </div>
            <DialogFooter className="mt-6">
              <DialogClose asChild>
                <Button variant="secondary" disabled={isPending}>
                  {t("common:CANCEL")}
                </Button>
              </DialogClose>
              <Button disabled={isPending || isLoading || isFetching || !isDirty}>{t("common:SAVE")}</Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
