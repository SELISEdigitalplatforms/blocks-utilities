import { useEffect, useMemo, useRef } from "react";
import { ChevronDown, Loader } from "lucide-react";
import { useLocation, useNavigate } from "react-router-dom";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { useGetProject, useGetProjects } from "@/hooks/use-project";
import { IProject } from "@/models/project.model";
import { useProjectStore } from "@/store/useProjectStore";

const redirectPaths: Record<string, string> = {
  "/services/iam/user-detail/*": "/services/iam",
  "/services/iam/role-detail/*": "/services/iam?tab=roles",
  "/services/iam/organization-detail/*": "/services/iam",
  "/services/iam/permission-detail/*": "/services/iam",
  "/services/authentication/sso-configuration": "/services/authentication?tab=social",
};

const wildcardToRegex = (pattern: string) => {
  const escaped = pattern.replace(/[-/\\^$+?.()|[\]{}]/g, "\\$&");
  return `^${escaped.replace(/\*/g, "[^/]+")}$`;
};

export function ProjectList() {
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const { data: projectGroups = [], isLoading } = useGetProjects();
  const { selectedProject, setSelectedProject } = useProjectStore();
  const { data: projectData } = useGetProject({ projectId: selectedProject?.itemId || "" });
  const pendingProjectRef = useRef<IProject | null>(null);

  const redirectRegexMap = useMemo(
    () =>
      Object.entries(redirectPaths).reduce<Record<string, string>>((acc, [pattern, target]) => {
        acc[wildcardToRegex(pattern)] = target;
        return acc;
      }, {}),
    [],
  );

  useEffect(() => {
    if (pendingProjectRef.current) {
      setSelectedProject(pendingProjectRef.current);
      pendingProjectRef.current = null;
    }
  }, [pathname, setSelectedProject]);

  const handleProjectSelect = (project: IProject) => {
    const redirectEntry = Object.entries(redirectRegexMap).find(([regex]) =>
      new RegExp(regex).test(pathname),
    );

    if (redirectEntry) {
      pendingProjectRef.current = project;
      navigate(redirectEntry[1], { replace: true });
      return;
    }

    setSelectedProject(project);
  };

  const name = projectData?.data.name || selectedProject?.name;
  const projects = projectGroups.map((group) => group.projects[0]).filter(Boolean);

  return (
    <DropdownMenu>
      <DropdownMenuTrigger className="w-full rounded-sm p-1 text-left hover:bg-accent hover:text-accent-foreground md:p-2">
        <div className="flex items-center justify-between gap-2">
          <div className="truncate text-sm font-medium">{name || "Select a Project"}</div>
          <ChevronDown className="h-4 w-4 shrink-0" />
        </div>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-[--radix-dropdown-menu-trigger-width]">
        <DropdownMenuLabel>Your Projects</DropdownMenuLabel>
        {projects
          .filter((project) => project.itemId !== selectedProject?.itemId)
          .slice(0, 5)
          .map((project) => (
            <DropdownMenuItem key={project.itemId} onSelect={() => handleProjectSelect(project)}>
              {isLoading ? (
                <div className="flex w-full items-center justify-center py-2">
                  <Loader size={16} className="animate-spin text-gray-400" />
                </div>
              ) : (
                <span>{project.name}</span>
              )}
            </DropdownMenuItem>
          ))}
        <DropdownMenuSeparator />
        <DropdownMenuItem disabled>Project overview is not part of this client</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}