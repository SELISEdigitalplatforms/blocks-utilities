import { useContext } from "react";
import { PanelLeft } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";
import { Notification } from "@/components/notification/notification";
import { ProjectList } from "@/components/project-list/project-list";
import { UserDropdownMenu } from "@/components/user-dropdown-menu/user-dropdown-menu";
import { EnvironmentList } from "@/components/environment-list/environment-list";
import { SidebarMobileView } from "@/layouts/sidebar-mobile-view/sidebar-mobile-view";
import { SidebarContext } from "@/contexts/dashboard-layout-provider";
import { BlocksAppLauncher } from "@/components/blocks-app-launcher/blocks-app-launcher";
import { cn } from "@/lib/utils";

export function DashboardHeader() {
  const { isSidebarOpen, toggleSidebar } = useContext(SidebarContext);

  return (
    <>
      <header className="flex h-[60px] items-center justify-between gap-4 border-b bg-background px-5 sm:px-6">
        <div className="md:hidden">
          <SidebarMobileView />
        </div>

        <div className="hidden items-center md:flex">
          <Button
            variant="ghost"
            size="icon"
            className={cn("hidden shrink-0 p-0", !isSidebarOpen && "inline-flex")}
            onClick={toggleSidebar}
          >
            <PanelLeft className="h-6 w-6" />
          </Button>
          <div className="w-52">
            <ProjectList />
          </div>
        </div>

        <div className="flex items-center gap-4">
          <div className="hidden h-fit max-w-40 md:flex">
            <EnvironmentList />
          </div>
          <ModeToggle />
          <Notification />
          <BlocksAppLauncher />
          <UserDropdownMenu />
        </div>
      </header>
      {/* Mobile project/environment selectors */}
      <div className="border-b bg-background px-5 sm:px-6 py-3 md:hidden">
        <div className="grid gap-3">
          <ProjectList />
          <EnvironmentList />
        </div>
      </div>
    </>
  );
}
