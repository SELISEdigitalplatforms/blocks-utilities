import {
  Card,
  CardContent,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { useNavigate } from "react-router-dom";
import { IProject } from "@blocks-identifier/models/project.model";
import { Badge } from "@/components/ui-kits/badge/badge";
import {
  Tooltip,
  TooltipProvider,
  TooltipTrigger,
  TooltipContent,
} from "@/components/ui-kits/tooltip/tooltip";
import { environmentOptions } from "@/constants/environment-options";
import { useProjectStore } from "@/store/useProjectStore";
import { Settings2 } from "lucide-react";

type ProjectCardProps = {
  project: IProject;
  projects: IProject[];
};

export const ProjectCard = ({ project, projects }: ProjectCardProps) => {
  const navigate = useNavigate();
  const { setTennantGroup, setSelectedProject } = useProjectStore();

  const onConfigureClick = () => {
    setTennantGroup(project.tenantGroupId);
    setSelectedProject(project);
    navigate("/project-overview/environments");
  };

  const onEnvBadgeClick = (e: React.MouseEvent, envProject: IProject) => {
    e.stopPropagation();
    setTennantGroup(envProject.tenantGroupId);
    setSelectedProject(envProject);
    navigate("/dashboard");
  };

  const envList = projects.map((p) => p.environment);

  const renderBadge = (env: string, envProject: IProject) => (
    <Badge
      key={env}
      variant="secondary"
      className="inline-flex cursor-pointer items-center text-xs transition-colors hover:bg-primary hover:text-primary-foreground"
      onClick={(e) => onEnvBadgeClick(e, envProject)}
    >
      {environmentOptions.find((option) => option.value === env)?.label}
    </Badge>
  );

  return (
    <Card className="group flex flex-col overflow-hidden rounded-xl border border-border/60 bg-card p-4 shadow-sm transition-all duration-200 hover:border-primary/30 hover:shadow-md h-[160px]">
      {/* Header: Project Name + Configure Button */}

      <div className="flex items-start justify-between gap-2 relative">
        <CardTitle className="line-clamp-3 break-all text-base font-semibold leading-snug flex-1 pr-2">
          {project.name}
        </CardTitle>
        <div className="absolute right-0 top-0">
          <TooltipProvider>
            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  size="icon"
                  variant="ghost"
                  className="h-8 w-8 flex-shrink-0 text-muted-foreground transition-colors hover:text-primary hover:bg-primary/10"
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

      {/* Environment Tags - Bottom */}
      <div className="mt-auto">
        {envList.length > 0 ? (
          envList.length > 3 ? (
            <TooltipProvider>
              <Tooltip>
                <TooltipTrigger asChild>
                  <div className="flex flex-wrap gap-1.5">
                    {projects.slice(0, 3).map((p) => renderBadge(p.environment, p))}
                    <Badge
                      variant="secondary"
                      className="inline-flex cursor-pointer items-center text-xs"
                    >
                      +{projects.length - 3}
                    </Badge>
                  </div>
                </TooltipTrigger>
                <TooltipContent>
                  <div className="flex flex-wrap gap-1">
                    {projects.map((p) => renderBadge(p.environment, p))}
                  </div>
                </TooltipContent>
              </Tooltip>
            </TooltipProvider>
          ) : (
            <div className="flex flex-wrap gap-1.5">
              {projects.map((p) => renderBadge(p.environment, p))}
            </div>
          )
        ) : (
          <Badge variant="secondary" className="inline-flex items-center text-xs">
            No environments
          </Badge>
        )}
      </div>
    </Card>
  );
};
