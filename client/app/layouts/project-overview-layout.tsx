import { Outlet } from "react-router-dom";
import { DashboardLayoutProvider } from "@/contexts/dashboard-layout-provider";
import {
  ImpersonationChecker,
  ImpersonationTerminator,
  ProtectedGuard,
} from "@/guards/protected-guard";
import { SidebarMenuDesktop } from "@/layouts/sidebar-menu-desktop/sidebar-menu-desktop";
import { DashboardHeader } from "@/layouts/dashboard-header/dashboard-header";

export function ProjectOverviewLayout() {
  return (
    <ProtectedGuard>
      <ImpersonationChecker>
        <ImpersonationTerminator>
          <DashboardLayoutProvider isOpen={true} persist>
            <div className="relative flex h-screen overflow-hidden bg-[hsl(var(--surface-app))]">
              <SidebarMenuDesktop />
              <div className="flex min-w-0 flex-1 flex-col overflow-hidden">
                <DashboardHeader />
                <main className="flex-1 overflow-y-auto overflow-x-hidden">
                  <Outlet />
                </main>
              </div>
            </div>
          </DashboardLayoutProvider>
        </ImpersonationTerminator>
      </ImpersonationChecker>
    </ProtectedGuard>
  );
}
