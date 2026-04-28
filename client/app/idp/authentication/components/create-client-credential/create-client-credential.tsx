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
import { useEffect, useState } from "react";
import { useProjectStore } from "@/store/useProjectStore";
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
  createClientSchema,
} from "./utils";
import { isErrorWithErrors } from "@/lib/error";

export const CreateClientCredential = () => {
  const [open, setOpen] = useState<boolean>(false);
  const [filter, setFilter] = useState<string>("");
  const [filteredRoles, setFilteredRoles] = useState<IRole[]>([]);
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

  useEffect(() => {
    if (data?.data) {
      setFilteredRoles(
        data.data.filter((role) => role.slug.toLowerCase().includes(filter.toLowerCase())),
      );
    } else {
      setFilteredRoles([]);
    }
  }, [filter, data]);

  const form = useForm({
    resolver: zodResolver(createClientSchema),
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
      showSuccessToast({ description: "Service Created successfully" });
      setOpen(false);
      return;
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Something went wrong" });
    } finally {
      form.reset();
    }
  };

  return (
    <Dialog open={open} onOpenChange={handleDialogOpenChange}>
      <DialogTrigger>
        <Button>
          <Plus className="aspect-square w-4" />
          <span className="sr-only sm:not-sr-only sm:ml-2">Create</span>
        </Button>
      </DialogTrigger>
      <DialogContent className="max-h-[90vh]">
        <DialogHeader>
          <DialogTitle>New Access Token</DialogTitle>
          <DialogDescription>Enter details to create a new key.</DialogDescription>
        </DialogHeader>

        <Form {...form}>
          <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-6">
            <FormField
              control={form.control}
              name="clientNameService"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Client Name</FormLabel>
                  <FormControl>
                    <Input placeholder="Enter client name" {...field} />
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
                  <FormLabel>Audience</FormLabel>
                  <FormControl>
                    <Input placeholder="Enter audience URL" {...field} />
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
                  <FormLabel>Assign Role(s)</FormLabel>

                  <div className="relative">
                    <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                    <Input
                      placeholder="Search by role name"
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
                          No roles found
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
                  Cancel
                </Button>
              </DialogClose>
              <Button disabled={isPending || !isDirty} type="submit">
                Add
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
