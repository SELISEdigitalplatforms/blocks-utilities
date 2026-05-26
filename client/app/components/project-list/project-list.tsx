import { useEffect, useMemo, useRef } from "react";
import { FolderOpen, Loader } from "lucide-react";
import { useLocation, useNavigate } from "react-router-dom";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import {
  useGetProject,
  useGetProjects,
} from "@blocks-identifier/hooks/use-project";
import { IProject } from "@blocks-identifier/models/project.model";
import { useProjectStore } from "@/store/useProjectStore";
const redirectPaths: Record<string, string> = {
  "/services/iam/user-detail/*": "/services/iam",
  "/services/iam/role-detail/*": "/services/iam?tab=roles",
  "/services/iam/organization-detail/*": "/services/iam",
  "/services/iam/permission-detail/*": "/services/iam",
  "/services/authentication/sso-configuration":
    "/services/authentication?tab=social",
};
const wildcardToRegex = (pattern: string) => {
  const escaped = pattern.replace(/[-/\\^$+?.()|[\]{}]/g, "\\$&");
  return `^${escaped.replace(/\*/g, "[^/]+")}$`;
};
export function ProjectList({ collapsed = false }: { collapsed?: boolean }) {
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const { data: projectGroups = [], isLoading } = useGetProjects();
  const {
    selectedProject,
    setSelectedProject,
    projects: storedProjects,
  } = useProjectStore();
  const { data: projectData } = useGetProject({
    projectId: selectedProject?.itemId || "",
  });
  const pendingProjectRef = useRef<IProject | null>(null);
  const redirectRegexMap = useMemo(
    () =>
      Object.entries(redirectPaths).reduce<Record<string, string>>(
        (acc, [pattern, target]) => {
          acc[wildcardToRegex(pattern)] = target;
          return acc;
        },
        {},
      ),
    [],
  );
  useEffect(() => {
    if (pendingProjectRef.current) {
      setSelectedProject(pendingProjectRef.current);
      pendingProjectRef.current = null;
      window.location.reload();
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
    window.location.reload();
  };
  const name = projectData?.data?.name || selectedProject?.name;
  const queryProjects = projectGroups
    .flatMap((group) => group.projects)
    .filter(Boolean);
  const projects =
    queryProjects.length > 0 ? queryProjects : storedProjects.filter(Boolean);
  return (
    <DropdownMenu>
      {collapsed ? (
        <DropdownMenuTrigger
          disabled
          className="group relative flex h-10 w-full items-center justify-center rounded-lg"
        >
          <FolderOpen className="h-5 w-5 text-muted-foreground" />
          <div className="pointer-events-none absolute left-full top-0 z-20 ml-2 min-w-max whitespace-nowrap rounded bg-gray-300 px-2 py-1 text-xs text-primary opacity-0 transition-opacity group-hover:opacity-100">
            {name || "Select a Project"}
          </div>
        </DropdownMenuTrigger>
      ) : (
        <DropdownMenuTrigger
          disabled
          className="w-full rounded-lg px-2 py-2 text-left cursor-default"
        >
          <div className="flex items-center gap-2.5">
            <FolderOpen className="h-4 w-4 shrink-0 text-muted-foreground" />
            <div className="min-w-0 flex-1">
              <div className="text-[11px] font-medium uppercase tracking-wide text-muted-foreground">
                Project
              </div>
              <div className="break-all text-sm font-medium leading-tight">
                {name || "Select a Project"}
              </div>
            </div>
            {/* <ChevronsUpDown className="h-3.5 w-3.5 shrink-0 text-muted-foreground" /> */}
          </div>
        </DropdownMenuTrigger>
      )}
      <DropdownMenuContent
        align={collapsed ? "center" : "start"}
        side={collapsed ? "right" : "bottom"}
        sideOffset={collapsed ? 8 : 4}
        className={
          collapsed ? "min-w-48" : "w-[--radix-dropdown-menu-trigger-width]"
        }
      >
        <DropdownMenuLabel>Your Projects</DropdownMenuLabel>
        {isLoading ? (
          <DropdownMenuItem disabled>
            <div className="flex w-full items-center justify-center py-1">
              <Loader size={16} className="animate-spin text-gray-400" />
            </div>
          </DropdownMenuItem>
        ) : (
          projects
            .filter((project) => project.itemId !== selectedProject?.itemId)
            .slice(0, 5)
            .map((project) => (
              <DropdownMenuItem
                key={project.itemId}
                onSelect={() => handleProjectSelect(project)}
              >
                <span>{project.name}</span>
              </DropdownMenuItem>
            ))
        )}
        <DropdownMenuSeparator />
        <DropdownMenuItem disabled>
          Project overview is not part of this client
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
