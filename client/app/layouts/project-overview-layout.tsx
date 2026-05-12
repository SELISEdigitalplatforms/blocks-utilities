import { Outlet } from "react-router-dom";
import { ProtectedGuard } from "@/guards/protected-guard";
import { ProjectGuard } from "@/guards/project-guard";
import { ConsoleHeader } from "@/layouts/console-header/console-header";
import { ProjectOverviewSidebarDesktop } from "@/layouts/project-overview-sidebar/project-overview-sidebar-desktop";
import { ProjectOverviewSidebarMobile } from "@/layouts/project-overview-sidebar/project-overview-sidebar-mobile";
import { BreadcrumbProvider } from "@/contexts/breadcrumb-context";

export function ProjectOverviewLayout() {
  return (
    <ProtectedGuard>
      <BreadcrumbProvider>
        <div className="relative flex min-h-screen flex-col bg-[hsl(var(--surface-app))]">
          <ConsoleHeader />
          <div className="flex pt-[59px]">
            <ProjectOverviewSidebarDesktop />
            <div className="flex min-w-0 flex-1 flex-col overflow-hidden">
              <ProjectOverviewSidebarMobile />
              <main className="flex-1 overflow-auto">
                <ProjectGuard>
                  <Outlet />
                </ProjectGuard>
              </main>
            </div>
          </div>
        </div>
      </BreadcrumbProvider>
    </ProtectedGuard>
  );
}
