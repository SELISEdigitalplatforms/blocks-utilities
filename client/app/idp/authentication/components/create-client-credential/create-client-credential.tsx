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
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Search } from "lucide-react";
import { zodResolver } from "@hookform/resolvers/zod";
import { useMemo, useState } from "react";
import { useProjectStore, useT } from "@seliseblocks/genesis-os";

import { useSaveAuthClient } from "@blocks-idp/authentication/hooks/use-auth-clients";
import { useForm } from "react-hook-form";
import { Plus } from "lucide-react";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { ISaveClientCredentialPayload } from "@blocks-idp/authentication/models/auth.oidc.model";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { IRole } from "@blocks-idp/iam/models/role";
import {
  CreateClientModalFormDefaultValues,
  CreateClientModalFormValues,
  makeClientSchema,
} from "./utils";
import { isErrorWithErrors } from "@/lib/error";

export const CreateClientCredential = () => {
  const { t } = useT("utilities.idp.authentication");
  const [open, setOpen] = useState<boolean>(false);
  const [filter, setFilter] = useState<string>("");
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { mutateAsync: saveServiceClient, isPending } = useSaveAuthClient({
    projectKey: tenantId,
  });

  const { data, isLoading } = useGetRoles({
    page: 0,
    pageSize: 0,
    projectKey: tenantId,
    sort: { property: "Name", isDescending: false },
    filter: {
      search: filter,
    },
  });

  const filteredRoles = useMemo<IRole[]>(() => {
    if (data?.data) {
      return data.data.filter((role) =>
        role.slug.toLowerCase().includes(filter.toLowerCase()),
      );
    }
    return [];
  }, [filter, data]);

  const form = useForm({
    resolver: zodResolver(makeClientSchema()),
    defaultValues: CreateClientModalFormDefaultValues,
  });

  const {
    formState: { isDirty },
  } = form;

  const handleDialogOpenChange = (isOpen: boolean) => {
    if (!isOpen) {
      form.reset();
      setFilter("");
    }
    setOpen(isOpen);
  };

  const onSubmit = async (data: CreateClientModalFormValues) => {
    try {
      const payload: ISaveClientCredentialPayload = {
        name: data.clientNameService,
        roles: data.roles,
        projectKey: tenantId,
      };

      const res = await saveServiceClient(payload);
      if (!res.isSuccess) return showErrorToast({ errors: res.error });
      showSuccessToast({ description: t("CREATE_CLIENT_CREDENTIAL.SERVICE_CREATED_SUCCESSFULLY") });
      setOpen(false);
      return;
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: t("CREATE_CLIENT_CREDENTIAL.SOMETHING_WENT_WRONG") });
    } finally {
      form.reset();
    }
  };

  return (
    <Dialog open={open} onOpenChange={handleDialogOpenChange}>
      <DialogTrigger>
        <Button>
          <Plus className="aspect-square w-4" />
          <span className="sr-only sm:not-sr-only sm:ml-2">{t("common:CREATE")}</span>
        </Button>
      </DialogTrigger>
      <DialogContent className="max-h-[90vh]">
        <DialogHeader>
          <DialogTitle>{t("CREATE_CLIENT_CREDENTIAL.NEW_ACCESS_TOKEN")}</DialogTitle>
          <DialogDescription>{t("CREATE_CLIENT_CREDENTIAL.ENTER_DETAILS_TO_CREATE_A")}</DialogDescription>
        </DialogHeader>

        <Form {...form}>
          <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-6">
            <FormField
              control={form.control}
              name="clientNameService"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("CREATE_CLIENT_CREDENTIAL.CLIENT_NAME")}</FormLabel>
                  <FormControl>
                    <Input placeholder={t("CREATE_CLIENT_CREDENTIAL.ENTER_CLIENT_NAME")} {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="audienceUrlService"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("CREATE_CLIENT_CREDENTIAL.AUDIENCE")}</FormLabel>
                  <FormControl>
                    <Input placeholder={t("CREATE_CLIENT_CREDENTIAL.ENTER_AUDIENCE_URL")} {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="roles"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("CREATE_CLIENT_CREDENTIAL.ASSIGN_ROLE_S")}</FormLabel>

                  <div className="relative">
                    <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                    <Input
                      placeholder={t("CREATE_CLIENT_CREDENTIAL.SEARCH_BY_ROLE_NAME")}
                      className="pl-9"
                      value={filter}
                      onChange={(e) => setFilter(e.target.value)}
                    />
                  </div>

                  <FormControl>
                    <div className="grid grid-cols-2 gap-4 rounded border p-3">
                      {filteredRoles?.map((type) => {
                        const isChecked = field.value?.includes(type.slug);

                        return (
                          <div key={type.slug} className="flex items-center gap-2">
                            <Checkbox
                              checked={isChecked}
                              onCheckedChange={(checked) => {
                                const updated = checked
                                  ? [...field.value, type.slug]
                                  : field.value.filter((role: string) => role !== type.slug);

                                field.onChange(updated);
                              }}
                            />
                            <label htmlFor={type.slug} className="cursor-pointer">
                              {type.slug}
                            </label>
                          </div>
                        );
                      })}

                      {isLoading && (
                        <div className="col-span-2 grid gap-2">
                          <Skeleton className="h-12 w-full rounded" />
                        </div>
                      )}

                      {!isLoading && filteredRoles?.length === 0 && (
                        <p className="col-span-2 py-2 text-center text-sm text-muted-foreground">
                          {t("CREATE_CLIENT_CREDENTIAL.NO_ROLES_FOUND")}
                        </p>
                      )}
                    </div>
                  </FormControl>
                </FormItem>
              )}
            />

            <DialogFooter>
              <DialogClose>
                <Button onClick={() => setOpen(false)} type="button" variant="outline">
                  {t("common:CANCEL")}
                </Button>
              </DialogClose>
              <Button disabled={isPending || !isDirty} type="submit">
                {t("common:ADD")}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
