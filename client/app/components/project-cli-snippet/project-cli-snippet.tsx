import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useGetProject } from "@/hooks/use-project";
import { useProjectStore } from "@/store/useProjectStore";
import { CopyableSnippet } from "@/components/copyable-snippet/copyable-snippet";
import { getProjectBlocksApiUrl } from "@/lib/domain";

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

export const ProjectCliSnippet = () => {
  const { itemId } = useProjectStore().selectedProject || { itemId: "", tenantId: "" };
  const { data, isLoading } = useGetProject({ projectId: itemId });

  const cliSetupCommand = "npm install -g @seliseblocks/cli";
  const blocksMicroservicesUrl = getProjectBlocksApiUrl(data?.data);
  const projectSetupCommand =
    `blocks new web ${data?.data.name.replaceAll(" ", "_").toLowerCase()} --x-blocks-key ${data?.data.tenantId} --app-domain ${data?.data.applicationDomain} --project-slug ${data?.data.tenantSlug || ""} --blocks-api-url ${blocksMicroservicesUrl}`.trim();

  if (isLoading) return <LoadingSkeleton />;
  return (
    <Card>
      <CardHeader>
        <CardTitle>Frontend Setup commands</CardTitle>
      </CardHeader>
      <CardContent>
        <div>
          If you have the Blocks CLI already installed, run:
          <CopyableSnippet code={projectSetupCommand} isCopyable={true} />
        </div>
        <div className="mt-6">
          If you haven&apos;t installed the CLI yet, install it globally using npm, then run the command:
          <CopyableSnippet code={`${cliSetupCommand} \n${projectSetupCommand}`} isCopyable={true} />
        </div>
      </CardContent>
    </Card>
  );
};
