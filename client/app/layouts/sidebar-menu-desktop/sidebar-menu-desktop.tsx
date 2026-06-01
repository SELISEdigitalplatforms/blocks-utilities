import { Fragment, useContext } from "react";
import { PanelLeft } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { DesktopMenuItem } from "@/components/menus/desktop-menu-item";
import { EnvironmentList } from "@/components/environment-list/environment-list";
import { Button } from "@/components/ui-kits/button/button";
import { ProjectList } from "@/components/project-list/project-list";
import { Separator } from "@/components/ui-kits/separator/separator";
import { navigationMenus } from "@/constants/navigation-menus";
import { SidebarContext } from "@/contexts/dashboard-layout-provider";
import { useFilteredMenus } from "@/hooks/use-filtered-menus";
import { cn } from "@/lib/utils";

export function SidebarMenuDesktop() {
  const { isSidebarOpen, toggleSidebar } = useContext(SidebarContext);
  const allowedMenu = useFilteredMenus(navigationMenus);
  const { pathname } = useLocation();
  const isProjectOverviewRoute = pathname.startsWith("/project-overview");

  return (
    <div
      className={cn(
        "hidden h-screen flex-col border-r bg-background transition-all md:flex",
        isSidebarOpen ? "w-60 overflow-hidden" : "w-14",
      )}
    >
      <div className="flex h-[60px] shrink-0 items-center justify-between border-b bg-background px-3">
        <Link
          to="/console"
          className={cn(
            "relative inline-block cursor-pointer overflow-hidden transition-all duration-300 ease-in-out",
            isSidebarOpen ? "h-[36px] w-[72px]" : "h-8 w-8",
          )}
        >
          {/* Expanded Light Logo */}
          <div
            className={cn(
              "absolute inset-0 bg-contain bg-no-repeat transition-all duration-300 ease-in-out dark:hidden",
              isSidebarOpen ? "opacity-100 scale-100" : "opacity-0 scale-75",
            )}
            style={{
              backgroundImage: "url('/utilities_logo_black.svg')",
            }}
          />
          {/* Expanded Dark Logo */}
          <div
            className={cn(
              "absolute inset-0 hidden bg-contain bg-no-repeat transition-all duration-300 ease-in-out dark:block",
              isSidebarOpen ? "opacity-100 scale-100" : "opacity-0 scale-75",
            )}
            style={{
              backgroundImage: "url('/utilities_logo_white.svg')",
            }}
          />
          {/* Collapsed Light Icon */}
          <div
            className={cn(
              "absolute inset-0 bg-contain bg-no-repeat transition-all duration-300 ease-in-out dark:hidden",
              isSidebarOpen ? "opacity-0 scale-75" : "opacity-100 scale-100",
            )}
            style={{
              backgroundImage: "url('/Icon.svg')",
            }}
          />
          {/* Collapsed Dark Icon */}
          <div
            className={cn(
              "absolute inset-0 hidden bg-contain bg-no-repeat transition-all duration-300 ease-in-out dark:block",
              isSidebarOpen ? "opacity-0 scale-75" : "opacity-100 scale-100",
            )}
            style={{
              backgroundImage: "url('/Icon_White.svg')",
            }}
          />
        </Link>

        {isSidebarOpen && (
          <Button
            variant="ghost"
            size="icon"
            className="shrink-0 p-0"
            onClick={toggleSidebar}
          >
            <PanelLeft className="h-6 w-6" />
          </Button>
        )}
      </div>

      {/* Workspace */}
      {!isProjectOverviewRoute &&
        (isSidebarOpen ? (
          <div className="border-b px-2 pb-2 pt-2">
            <p className="mb-1 px-2 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
              Workspace
            </p>

            <div className="space-y-0.5">
              <ProjectList />
              <EnvironmentList />
            </div>
          </div>
        ) : (
          <div className="border-b py-1">
            <ProjectList collapsed />
            <EnvironmentList collapsed />
          </div>
        ))}

      {/* Navigation - Hidden on project overview routes */}
      {!isProjectOverviewRoute && (
        <div className="w-full flex-1">
          <nav className="grid w-full items-start gap-1 text-sm">
            {allowedMenu.map((menu) => (
              <Fragment key={menu.id}>
                {menu.type === "menu" ? (
                  <DesktopMenuItem menu={menu} isSidebarOpen={isSidebarOpen} />
                ) : (
                  <Separator />
                )}
              </Fragment>
            ))}
          </nav>
        </div>
      )}
    </div>
  );
}
