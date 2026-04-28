import { Link } from "react-router-dom";
import { Plus } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { useProjectStore } from "@/store/useProjectStore";
import { useGetEnvRepositories, useGetProject } from "@/hooks/use-project";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Button } from "@/components/ui-kits/button/button";
import { CopyableSnippet } from "@/components/copyable-snippet/copyable-snippet";

const LoadingSkeleton = () => (
  <Card>
    <CardContent>
      <div>
        <Skeleton className="h-8 w-1/4" />
        <Skeleton className="mt-2 h-6 w-full" />
      </div>
      <div className="mt-6">
        <Skeleton className="h-8 w-1/4" />
        <Skeleton className="mt-2 h-6 w-full" />
      </div>
    </CardContent>
  </Card>
);

export const GitCommandSnippet = () => {
  const { itemId } = useProjectStore().selectedProject || { itemId: "", tenantId: "" };
  const { data, isLoading } = useGetProject({ projectId: itemId });
  const {
    data: envRepositoriesResponse,
    isLoading: isLoadingEnvRepos,
    isFetching: isFetchingEnvRepos,
  } = useGetEnvRepositories(itemId || "");

  if (isLoading || isLoadingEnvRepos || isFetchingEnvRepos) return <LoadingSkeleton />;
  const branchName = data?.data.environment === "prod" ? "main" : data?.data.environment;
  const repo = envRepositoriesResponse?.data?.find(
    (repo) =>
      repo.defaultDeploymentUrl === data?.data.applicationDomain ||
      repo.customDeploymentUrl === data?.data.applicationDomain,
  );
  const repoLink = repo?.repoUrl || "<your-repo-link>";
  const hasRepository = !!repo?.repoUrl;
  const gitCommands = `git remote add origin ${repoLink}\ngit branch -M ${branchName}\ngit add .\ngit commit -m "feat: initiate project"\ngit push -u origin ${branchName}`;

  return (
    <Card className="mb-6">
      <CardHeader>
        <CardTitle>Git Commands</CardTitle>
      </CardHeader>
      <CardContent>
        {!hasRepository && (
          <div className="flex flex-col items-center justify-between gap-3 rounded-sm border border-base-error bg-blocks-error-100 px-4 py-4 text-base font-normal text-blocks-error-800 md:flex-row md:gap-4">
            <p>
              Please add a repository and set the Application Domain above to enable git commands.
            </p>
            <Link to="/project-overview/repositories">
              <Button size="sm">
                <Plus className="mr-2 h-4 w-4" />
                Add Repository
              </Button>
            </Link>
          </div>
        )}
        <div className={!hasRepository ? "pointer-events-none mt-4 select-none opacity-50" : ""}>
          <CopyableSnippet code={gitCommands} isCopyable={hasRepository} />
        </div>
      </CardContent>
    </Card>
  );
};
