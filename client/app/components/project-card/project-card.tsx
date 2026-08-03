import {
  Card,
  CardTitle,
} from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { useNavigate } from "react-router";
import { IProject } from "@blocks-identifier/models/project.model";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
import { environmentOptions } from "@/constants/environment-options";
import { useProjectStore } from "@seliseblocks/genesis-os";
import {
  useScopedPath,
  useStartImpersonation,
} from "@seliseblocks/genesis-os/hooks";

import { ChevronRight, Settings2 } from "lucide-react";

type ProjectCardProps = {
  project: IProject;
  projects: IProject[];
};

export const ProjectCard = ({ project, projects }: ProjectCardProps) => {
  const navigate = useNavigate();
  const { setTenantGroup, setSelectedProject } = useProjectStore();
  const scoped = useScopedPath();
  const { mutateAsync: startImpersonation } = useStartImpersonation();

  const onConfigureClick = () => {
    setTenantGroup(project.tenantGroupId);
    setSelectedProject(project);
    navigate(`/app/project/${project.tenantGroupId}/environments`);
  };

  const onEnvBadgeClick = async (
    e: React.MouseEvent,
    envProject: IProject,
  ) => {
    e.stopPropagation();
    try {
      await startImpersonation({ targeted_tenant_id: envProject.tenantId });
      setTenantGroup(envProject.tenantGroupId);
      setSelectedProject(envProject);
      navigate(scoped("magic-url"));
    } catch (error) {
      console.error("Failed to switch environment", error);
    }
  };

  const envList = projects.map((p) => p.environment);

  const renderEnvChip = (env: string, envProject: IProject) => {
    const label = environmentOptions.find((o) => o.value === env)?.label;
    return (
      <button
        key={env}
        onClick={(e) => onEnvBadgeClick(e, envProject)}
        className="group/chip inline-flex cursor-pointer items-center gap-1 rounded-full border border-primary bg-primary px-2.5 py-0.5 text-xs font-medium text-primary-foreground hover:border-[hsl(var(--blocks-primary-50))] hover:bg-[hsl(var(--blocks-primary-25))] hover:text-primary active:scale-95"
      >
        {label}
        <ChevronRight className="h-3 w-3 group-hover/chip:translate-x-0.5" />
      </button>
    );
  };

  return (
    <Card className="group flex h-[160px] flex-col overflow-hidden rounded-xl border border-border/60 bg-card p-4 shadow-sm hover:border-primary/30 hover:shadow-md">
      <div className="relative flex items-start justify-between gap-2">
        <CardTitle className="line-clamp-3 flex-1 break-all pr-2 text-base font-semibold leading-snug">
          {project.name}
        </CardTitle>
        <div className="absolute right-0 top-0">
          <TooltipProvider>
            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  size="icon"
                  variant="ghost"
                  className="h-8 w-8 flex-shrink-0 text-primary transition-colors hover:bg-primary/10"
                  onClick={onConfigureClick}
                >
                  <Settings2 size={16} />
                </Button>
              </TooltipTrigger>
              <TooltipContent>Configure Project</TooltipContent>
            </Tooltip>
          </TooltipProvider>
        </div>
      </div>

      <div className="mt-auto">
        {envList.length > 0 ? (
          envList.length > 3 ? (
            <TooltipProvider>
              <Tooltip>
                <TooltipTrigger asChild>
                  <div className="flex flex-wrap gap-1.5">
                    {projects.slice(0, 3).map((p) => renderEnvChip(p.environment, p))}
                    <span className="inline-flex cursor-pointer items-center rounded-full border border-border bg-transparent px-2.5 py-0.5 text-xs font-medium text-muted-foreground">
                      +{projects.length - 3}
                    </span>
                  </div>
                </TooltipTrigger>
                <TooltipContent>
                  <div className="flex flex-wrap gap-1">
                    {projects.map((p) => renderEnvChip(p.environment, p))}
                  </div>
                </TooltipContent>
              </Tooltip>
            </TooltipProvider>
          ) : (
            <div className="flex flex-wrap gap-1.5">
              {projects.map((p) => renderEnvChip(p.environment, p))}
            </div>
          )
        ) : (
          <span className="inline-flex items-center text-xs text-muted-foreground">
            No environments
          </span>
        )}
      </div>
    </Card>
  );
};
