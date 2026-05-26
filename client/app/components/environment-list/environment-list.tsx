import { useEffect, useMemo, useRef } from "react";
import { Globe, Loader } from "lucide-react";
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
export function EnvironmentList({
  collapsed = false,
}: {
  collapsed?: boolean;
}) {
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const { data: projectGroups = [], isLoading } = useGetProjects();
  const { selectedProject, setSelectedProject } = useProjectStore();
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
  useEffect(() => {
    if (
      projectData?.data &&
      selectedProject?.itemId === projectData.data?.itemId
    ) {
      setSelectedProject(projectData.data);
    }
  }, [projectData, selectedProject?.itemId, setSelectedProject]);
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
  const environment =
    projectData?.data?.environment || selectedProject?.environment;
  const applicationDomain =
    projectData?.data?.applicationDomain || selectedProject?.applicationDomain;
  const projects = useMemo(() => {
    if (!selectedProject) return [];
    const groupWithSelected = projectGroups.find((group) =>
      group.projects.some(
        (project) => project.itemId === selectedProject.itemId,
      ),
    );
    return groupWithSelected ? groupWithSelected.projects : [];
  }, [projectGroups, selectedProject]);
  return (
    <DropdownMenu>
      {collapsed ? (
        <DropdownMenuTrigger
          disabled
          className="group relative flex h-10 w-full items-center justify-center rounded-lg"
        >
          <Globe className="h-5 w-5 text-muted-foreground" />
          {environment && (
            <span className="absolute right-2 top-2 h-2 w-2 rounded-full bg-[hsl(var(--blocks-primary-50))] ring-1 ring-background" />
          )}
          <div className="pointer-events-none absolute left-full top-0 z-20 ml-2 min-w-max whitespace-nowrap rounded bg-gray-300 px-2 py-1 text-xs text-primary opacity-0 transition-opacity group-hover:opacity-100">
            {environment
              ? `${environment}${applicationDomain ? ` · ${applicationDomain}` : ""}`
              : "Select an Environment"}
          </div>
        </DropdownMenuTrigger>
      ) : (
        <DropdownMenuTrigger
          disabled
          className="w-full rounded-lg px-2 py-2 text-left cursor-default"
        >
          <div className="flex items-center gap-2.5">
            <Globe className="h-4 w-4 shrink-0 text-muted-foreground" />
            <div className="flex min-w-0 flex-1 flex-col items-start">
              <div className="text-[11px] font-medium uppercase tracking-wide text-muted-foreground">
                Environment
              </div>
              {environment ? (
                <div className="flex min-w-0 items-center gap-1 leading-tight">
                  <span className="shrink-0 rounded-sm bg-[hsl(var(--blocks-primary-50))] px-1.5 py-0.5 text-[11px] font-semibold leading-none text-[hsl(var(--high-emphasis))]">
                    {environment}
                  </span>
                  {/* <span className="min-w-0 truncate text-xs text-muted-foreground">
                    {applicationDomain || ""}
                  </span> */}
                </div>
              ) : (
                <span className="text-sm leading-tight">
                  Select an Environment
                </span>
              )}
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
        <DropdownMenuLabel>Your Environments</DropdownMenuLabel>
        {(() => {
          const others = projects
            .filter((project) => project.itemId !== selectedProject?.itemId)
            .slice(0, 5);
          if (others.length === 0) {
            return (
              <DropdownMenuItem disabled>
                <span className="text-xs text-muted-foreground">
                  No other environments available
                </span>
              </DropdownMenuItem>
            );
          }
          return others.map((project) => (
            <DropdownMenuItem
              key={project.itemId}
              onSelect={() => handleProjectSelect(project)}
            >
              {isLoading ? (
                <div className="flex w-full items-center justify-center py-2">
                  <Loader size={16} className="animate-spin text-gray-400" />
                </div>
              ) : (
                <span>{project.environment}</span>
              )}
            </DropdownMenuItem>
          ));
        })()}
        <DropdownMenuSeparator />
        <DropdownMenuItem disabled>
          Environment overview is not part of this client
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
