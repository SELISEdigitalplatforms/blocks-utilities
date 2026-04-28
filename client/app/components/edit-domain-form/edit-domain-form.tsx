import { useForm } from "react-hook-form";
import { z } from "zod";
import { zodResolver } from "@hookform/resolvers/zod";
import { useUpdateRepositories } from "@/hooks/use-project";
import { useProjectStore } from "@/store/useProjectStore";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Input } from "@/components/ui-kits/input/input";
import { IEnvRepository } from "@blocks-identifier/models/project.model";
import { getDomain, getSubdomain } from "@/lib/domain";
import { DialogClose, DialogFooter } from "@/components/ui-kits/dialog/dialog";
import { Button } from "@/components/ui-kits/button/button";
import { editDomainFormSchema } from "./utils";

type EditDomainFormProps = {
  customDomain: string;
  repositories: IEnvRepository[];
  onAfterSubmit: () => void;
};

const editDomainFormDefaultValue = {
  domains: [] as { itemId: string; customDeploymentUrl: string; repoUrl: string }[],
};

export const EditDomainForm = ({
  customDomain,
  repositories,
  onAfterSubmit,
}: EditDomainFormProps) => {
  const { mutateAsync, isPending } = useUpdateRepositories();
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const environment = useProjectStore().selectedProject?.environment || "dev";
  const form = useForm({
    defaultValues: editDomainFormDefaultValue,
    values:
      repositories.length > 0
        ? {
            domains: repositories.map((repo) => ({
              itemId: repo.itemId,
              repoUrl: repo.repoUrl || "",
              customDeploymentUrl: getSubdomain(repo.customDeploymentUrl) || "",
            })),
          }
        : undefined,
    resolver: zodResolver(editDomainFormSchema),
  });

  if (repositories.length === 0) {
    return <> </>;
  }
  const MAIN_DOMAIN = getDomain(customDomain || "");
  const onSubmitHandler = async (values: z.infer<typeof editDomainFormSchema>) => {
    try {
      const repoWithDomains = values.domains.map((domain) => ({
        repoId: domain.itemId,
        repoUrl: domain.repoUrl || "",
        customDeploymentDomain: `${domain.customDeploymentUrl}.${MAIN_DOMAIN}`,
      }));
      const res = await mutateAsync({
        projectKey: tenantId,
        projectEnv: environment,
        repoWithDomains: repoWithDomains,
      });
      if (res.isSuccess) {
        showSuccessToast({ description: "Project is updated successfully" });
        form.reset();
        onAfterSubmit();
      } else {
        showErrorToast({ errors: res.errors });
      }
    } catch (error) {
      if (error && typeof error === "object" && "errors" in error) {
        showErrorToast({ errors: (error as any).errors });
      }
    }
  };
  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(onSubmitHandler)} className="flex flex-col gap-4">
        {repositories.map((repository, index) => (
          <div key={repository.itemId}>
            <FormField
              control={form.control}
              name={`domains.${index}.itemId`}
              render={({ field }) => <input type="hidden" {...field} value={repository.itemId} />}
            />
            <FormField
              control={form.control}
              name={`domains.${index}.customDeploymentUrl`}
              render={({ field }) => (
                <FormItem>
                  <FormLabel className="flex items-center gap-3">{repository.repoName}</FormLabel>
                  <FormControl>
                    <div className="flex items-center rounded-md border border-input bg-background px-3 py-2">
                      <Input
                        {...field}
                        placeholder="Custom sub domain"
                        className="h-auto border-0 bg-transparent p-0 focus-visible:ring-0 focus-visible:ring-offset-0"
                      />
                      <span className="ml-1 text-muted-foreground">.{MAIN_DOMAIN}</span>
                    </div>
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          </div>
        ))}
        <DialogFooter className="flex flex-row justify-end gap-2">
          <DialogClose asChild>
            <Button variant="outline" className="w-20">
              Cancel
            </Button>
          </DialogClose>
          <Button disabled={isPending} className="w-20" type="submit">
            Save
          </Button>
        </DialogFooter>
      </form>
    </Form>
  );
};
