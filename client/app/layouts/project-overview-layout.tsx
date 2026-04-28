import { Outlet } from "react-router-dom";
import { ProjectOverviewSidebarDesktop } from "@/layouts/project-overview-sidebar/project-overview-sidebar-desktop";
import { ProjectOverviewSidebarMobile } from "@/layouts/project-overview-sidebar/project-overview-sidebar-mobile";
import { ProjectGuard } from "@/guards/project-guard";
import { ConsoleHeader } from "@/layouts/console-header/console-header";
import { ProtectedGuard } from "@/guards/protected-guard";

export function ProjectOverviewLayout() {
  return (
    <ProtectedGuard>
      <div className="relative min-h-screen bg-[hsl(var(--surface-app))]">
        <ConsoleHeader />
        <ProjectGuard>
          <div className="flex h-screen overflow-hidden pt-14 lg:pt-[60px]">
            <ProjectOverviewSidebarDesktop />
            <div className="flex min-h-0 flex-1 flex-col overflow-hidden">
              <div className="flex items-center gap-2 border-b px-4 py-3 md:hidden">
                <ProjectOverviewSidebarMobile />
                <span className="text-sm font-medium">Project Overview</span>
              </div>
              <div className="flex-1 overflow-auto bg-[hsl(var(--surface-app))]">
                <Outlet />
              </div>
            </div>
          </div>
        </ProjectGuard>
      </div>
    </ProtectedGuard>
  );
}
