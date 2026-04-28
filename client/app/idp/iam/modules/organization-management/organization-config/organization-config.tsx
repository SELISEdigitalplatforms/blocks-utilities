
import { useEffect, useState } from "react";
import { useForm, SubmitHandler } from "react-hook-form";
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
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { zodResolver } from "@hookform/resolvers/zod";
import { Form, FormControl, FormField, FormItem, FormLabel } from "@/components/ui-kits/form/form";
import { useProjectStore } from "@/store/useProjectStore";
import { useSaveOrganizationConfig } from "@blocks-idp/iam/hooks/use-organization";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import {
  IOrganizationConfigForm,
  IOrganizationConfigResponse,
  organizationConfigFormDefaultValues,
  organizationConfigFormSchema,
} from "@blocks-idp/iam/models/organization-config.model";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui-kits/command/command";
import { Badge } from "@/components/ui-kits/badge/badge";
import { cn } from "@/lib/utils";
import { ChevronsUpDown, Check, Settings } from "lucide-react";

interface OrganizationConfigProps {
  configData: IOrganizationConfigResponse | null | undefined;
  isLoading: boolean;
}

export const OrganizationConfig = ({ configData, isLoading }: OrganizationConfigProps) => {
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [selectedRoles, setSelectedRoles] = useState<string[]>([]);
  const tenantId = useProjectStore().selectedProject?.tenantId || "";

  const { mutateAsync, isPending } = useSaveOrganizationConfig();

  const { data: rolesData, isLoading: isRolesLoading } = useGetRoles({
    projectKey: tenantId,
    page: 0,
    pageSize: 1000,
    sort: { property: "Name", isDescending: false },
    filter: { search: "" },
  });

  const roles = rolesData?.data || [];
  const roleOptions = roles.map((role) => ({
    label: role.name,
    value: role.slug,
  }));

  const form = useForm<IOrganizationConfigForm>({
    defaultValues: organizationConfigFormDefaultValues,
    resolver: zodResolver(organizationConfigFormSchema),
  });

  const {
    formState: { isDirty },
  } = form;

  const isMultiOrgEnabled = form.watch("isMultiOrgEnabled");

  const handleModalOpenChange = (value: boolean) => {
    if (!value) {
      form.reset({
        isMultiOrgEnabled: configData?.isMultiOrgEnabled ?? false,
        allowCreationFromCloud: configData?.allowCreationFromCloud ?? true,
        allowCreationFromConstruct: configData?.allowCreationFromConstruct ?? false,
      });
      setSelectedRoles(configData?.roles ?? []);
    }
    setIsModalOpen(value);
  };

  // Populate form when config data is loaded
  useEffect(() => {
    if (configData) {
      form.reset({
        isMultiOrgEnabled: configData.isMultiOrgEnabled ?? false,
        allowCreationFromCloud: configData.allowCreationFromCloud ?? true,
        allowCreationFromConstruct: configData.allowCreationFromConstruct ?? false,
      });
      setSelectedRoles(configData.roles ?? []);
    }
  }, [configData, form]);

  // Reset dependent fields when isMultiOrgEnabled is toggled off
  useEffect(() => {
    if (!isMultiOrgEnabled) {
      form.setValue("allowCreationFromCloud", true);
      form.setValue("allowCreationFromConstruct", false);
    }
  }, [isMultiOrgEnabled, form]);

  const onSubmit: SubmitHandler<IOrganizationConfigForm> = async (data) => {
    try {
      const res = await mutateAsync({
        itemId: configData?.itemId || "",
        allowCreationFromCloud: data.isMultiOrgEnabled ? data.allowCreationFromCloud : true,
        allowCreationFromConstruct: data.isMultiOrgEnabled
          ? data.allowCreationFromConstruct
          : false,
        isMultiOrgEnabled: data.isMultiOrgEnabled,
        roles: data.isMultiOrgEnabled && data.allowCreationFromConstruct ? selectedRoles : [],
        projectKey: tenantId,
      });
      if (!res.isSuccess) {
        showErrorToast({ errors: res.errors });
        return;
      }
      showSuccessToast({ description: "Organization config saved successfully" });
      setIsModalOpen(false);
    } catch (error: unknown) {
      if (error && typeof error === "object" && "errors" in error) {
        showErrorToast({ errors: error.errors });
      }
    }
  };

  return (
    <Dialog open={isModalOpen} onOpenChange={handleModalOpenChange}>
      <DialogTrigger asChild>
        <Button size="sm" variant="outline">
          <Settings className="h-5 w-5" />
          <span className="sr-only sm:not-sr-only sm:ml-2.5">Configure Organization</span>
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader className="mb-4">
          <DialogTitle>Organization Configuration</DialogTitle>
          <DialogDescription>Configure organization settings for your project.</DialogDescription>
        </DialogHeader>
        {isLoading ? (
          <div className="flex items-center justify-center py-8">Loading...</div>
        ) : (
          <Form {...form}>
            <form onSubmit={form.handleSubmit(onSubmit)} className="flex flex-col gap-4">
              <FormField
                name="isMultiOrgEnabled"
                control={form.control}
                render={({ field }) => (
                  <FormItem className="flex items-center gap-2">
                    <FormControl>
                      <Checkbox
                        className="h-5 w-5"
                        checked={field.value}
                        onCheckedChange={field.onChange}
                      />
                    </FormControl>
                    <FormLabel className="!mt-0">Enable Multi-Organization</FormLabel>
                  </FormItem>
                )}
              />

              {isMultiOrgEnabled && (
                <div className="ml-6 flex flex-col gap-3 pl-4">
                  <FormField
                    name="allowCreationFromCloud"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem className="flex items-center gap-2">
                        <FormControl>
                          <Checkbox
                            className="h-5 w-5"
                            checked={field.value}
                            onCheckedChange={field.onChange}
                          />
                        </FormControl>
                        <FormLabel className="!mt-0">Allow Creation From Cloud</FormLabel>
                      </FormItem>
                    )}
                  />

                  <FormField
                    name="allowCreationFromConstruct"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem className="flex items-center gap-2">
                        <FormControl>
                          <Checkbox
                            className="h-5 w-5"
                            checked={field.value}
                            onCheckedChange={field.onChange}
                            disabled={true}
                          />
                        </FormControl>
                        <FormLabel className="!mt-0">Allow Creation From Construct</FormLabel>
                      </FormItem>
                    )}
                  />

                  {form.watch("allowCreationFromConstruct") && (
                    <div className="mt-3 space-y-2">
                      <label className="text-sm font-medium">Default Roles</label>
                      {isRolesLoading ? (
                        <div className="p-2 text-sm text-muted-foreground">Loading roles...</div>
                      ) : roles.length === 0 ? (
                        <div className="p-2 text-sm text-muted-foreground">No roles available</div>
                      ) : (
                        <Popover>
                          <PopoverTrigger asChild>
                            <Button
                              variant="outline"
                              role="combobox"
                              className="w-full justify-between"
                            >
                              {selectedRoles.length > 0 ? (
                                <div className="flex flex-wrap gap-1">
                                  {selectedRoles.length > 2 ? (
                                    <Badge variant="secondary">
                                      {selectedRoles.length} selected
                                    </Badge>
                                  ) : (
                                    selectedRoles.map((slug) => {
                                      const role = roles.find((r) => r.slug === slug);
                                      return (
                                        <Badge key={slug} variant="secondary">
                                          {role?.name || slug}
                                        </Badge>
                                      );
                                    })
                                  )}
                                </div>
                              ) : (
                                "Select Roles"
                              )}
                              <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
                            </Button>
                          </PopoverTrigger>
                          <PopoverContent className="w-full p-0" align="start">
                            <Command>
                              <CommandInput placeholder="Search roles..." />
                              <CommandList>
                                <CommandEmpty>No roles found.</CommandEmpty>
                                <CommandGroup>
                                  {roleOptions.map((option) => {
                                    const isSelected = selectedRoles.includes(option.value);
                                    return (
                                      <CommandItem
                                        key={option.value}
                                        onSelect={() => {
                                          const newSelected = isSelected
                                            ? selectedRoles.filter((v) => v !== option.value)
                                            : [...selectedRoles, option.value];
                                          setSelectedRoles(newSelected);
                                        }}
                                      >
                                        <Check
                                          className={cn(
                                            "mr-2 h-4 w-4",
                                            isSelected ? "opacity-100" : "opacity-0",
                                          )}
                                        />
                                        {option.label}
                                      </CommandItem>
                                    );
                                  })}
                                </CommandGroup>
                              </CommandList>
                            </Command>
                          </PopoverContent>
                        </Popover>
                      )}
                    </div>
                  )}
                </div>
              )}

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
        )}
      </DialogContent>
    </Dialog>
  );
};
