import { useCallback, useEffect } from "react";
import { useProjectStore } from "@/store/useProjectStore";
import { useGetProject, useValidateCNameProject } from "@/hooks/use-project";
import { getDomain } from "@/lib/domain";
import { showErrorToast } from "@/hooks/use-toast";
import { ProjectDetail } from "@/components/project-detail/project-detail";
import { ProjectRepoList } from "@/components/project-repo-list/project-repo-list";
import { ProjectCliSnippet } from "@/components/project-cli-snippet/project-cli-snippet";
import { GitCommandSnippet } from "@/components/git-command-snippet/git-command-snippet";
import { ActionsListProject } from "@/components/actions-list-project/actions-list-project";

export const DashboardOverview = () => {
  const projectKey = useProjectStore().selectedProject?.tenantId || "";
  const { itemId } = useProjectStore().selectedProject || { itemId: "", tenantId: "" };
  const { data, isLoading } = useGetProject({ projectId: itemId });
  const { mutateAsync } = useValidateCNameProject({ projectKey });

  const cNameValidator = useCallback(async () => {
    try {
      if (
        !data?.data.applicationDomain ||
        getDomain(data.data.applicationDomain) === "seliseblocks.com"
      )
        return;

      await mutateAsync({
        projectKey: projectKey,
        cookieDomain: new URL(data?.data.customDomain).hostname,
      });
    } catch (error) {
      if (error && typeof error === "object" && "errors" in error) {
        showErrorToast({ errors: (error as any).errors });
      }
    }
  }, [data?.data.applicationDomain, data?.data.customDomain, mutateAsync, projectKey]);

  useEffect(() => {
    cNameValidator();
  }, [cNameValidator]);

  return (
    <main className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between gap-2">
        <h1 className="text-xl font-semibold md:text-2xl">Environment Overview</h1>
        <ActionsListProject />
      </div>
      <ProjectDetail project={data?.data} isLoading={isLoading} />
      <ProjectRepoList project={data?.data} isLoading={isLoading} />
      <ProjectCliSnippet />
      <GitCommandSnippet />
    </main>
  );
};
