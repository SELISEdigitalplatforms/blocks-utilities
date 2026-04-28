import { ReactNode } from "react";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { Button } from "@/components/ui-kits/button/button";
import { formatFullDate } from "@/lib/utils";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { MaskedText } from "@/components/masked-text";
import { IProject } from "@blocks-identifier/models/project.model";
import { environmentOptions } from "@/constants/environment-options";
import { getProjectBlocksApiUrl } from "@/lib/domain";

interface ProjectDetailItemProps {
  label: string;
  children: ReactNode;
}

const ProjectDetailItem = ({ label, children }: ProjectDetailItemProps) => (
  <div className="space-y-1.5">
    <div className="text-sm text-muted-foreground">{label}</div>
    <div className="text-base">{children}</div>
  </div>
);

const RenderProjectUrl = ({ project }: { project?: IProject }) => {
  if (!project) return null;
  const url = getProjectBlocksApiUrl(project);
  return (
    <div>
      <CopyToClipboardButton textToCopy={url || ""} isHoverable>
        {url}
      </CopyToClipboardButton>
    </div>
  );
};

const LoadingSkeleton = () => {
  return (
    <div className="mt-6 rounded-lg border bg-card px-2 py-2 shadow-sm md:mt-0">
      <div className="grid-col-1 grid gap-3 px-2 py-4 md:grid-cols-2 md:gap-4 lg:gap-6">
        {Array.from({ length: 6 }).map((_item, index) => (
          <div key={index}>
            <Skeleton className="h-5 w-1/2" />
            <Skeleton className="mt-2 h-5 w-full" />
          </div>
        ))}
      </div>
    </div>
  );
};

export const ProjectDetail = ({
  project,
  isLoading,
}: {
  project?: IProject;
  isLoading: boolean;
}) => {
  if (isLoading) return <LoadingSkeleton />;

  return (
    <div className="rounded-sm border bg-card px-2 py-2 shadow-sm">
      <div className="grid-col-1 grid gap-4 px-2 py-4 md:gap-6 lg:grid-cols-2">
        <ProjectDetailItem label="Name">{project?.name}</ProjectDetailItem>
        <ProjectDetailItem label="X-Blocks-Key">
          <div className="flex h-6 items-center gap-2">
            <CopyToClipboardButton textToCopy={project?.tenantId || ""} isHoverable>
              <MaskedText text={project?.tenantId || ""} showFirstN={3} showLastN={3} length={20} />
            </CopyToClipboardButton>
          </div>
        </ProjectDetailItem>
        {project?.tenantSlug && (
          <ProjectDetailItem label="Project Slug">
            <div className="flex h-6 items-center gap-2">
              <CopyToClipboardButton textToCopy={project?.tenantSlug || ""} isHoverable>
                {project?.tenantSlug}
              </CopyToClipboardButton>
            </div>
          </ProjectDetailItem>
        )}
        <ProjectDetailItem label="Environment">
          {project?.environment === "prod" ? (
            <Button className="mr-4 h-6 rounded-xl" size="sm" variant="default">
              Production
            </Button>
          ) : (
            <Button
              className="h-6 rounded-xl bg-blocks-btn-secondary hover:bg-blocks-btn-secondary/80"
              size="sm"
              variant="secondary"
            >
              {environmentOptions.find((option) => option.value === project?.environment)?.label}
            </Button>
          )}
        </ProjectDetailItem>
        <ProjectDetailItem label="Blocks Microservices Url">
          <div className="flex h-auto items-center gap-2">
            <RenderProjectUrl project={project} />
          </div>
        </ProjectDetailItem>
        <ProjectDetailItem label="Last updated Date">
          {project?.lastUpdatedDate && formatFullDate(new Date(project.lastUpdatedDate))}
        </ProjectDetailItem>
        <ProjectDetailItem label="Created Date">
          {project?.createdDate && formatFullDate(new Date(project.createdDate))}
        </ProjectDetailItem>
      </div>
    </div>
  );
};
