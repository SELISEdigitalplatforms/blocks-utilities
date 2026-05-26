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
import { useTheme } from "@/hooks/use-theme";
export function SidebarMenuDesktop() {
  const { isSidebarOpen, toggleSidebar } = useContext(SidebarContext);
  const { resolvedTheme } = useTheme();
  const allowedMenu = useFilteredMenus(navigationMenus);
  const { pathname } = useLocation();
  const isProjectOverviewRoute = pathname.startsWith("/project-overview");
  const getLogoSrc = () => {
    if (isSidebarOpen) {
      return resolvedTheme === "dark"
        ? "/utilities_logo_white.svg"
        : "/utilities_logo_black.svg";
    }
    return resolvedTheme === "dark" ? "/Icon_White.svg" : "/Icon.svg";
  };
  return (
    <div
      className={`hidden h-[calc(100vh)] flex-col border-r bg-background transition-all md:flex ${isSidebarOpen ? "w-60 overflow-hidden" : "w-14"}`}
    >
      <div className="flex h-[60px] shrink-0 items-center justify-between border-b bg-background px-3">
        <Link
          to="/console"
          className={cn(
            "relative inline-block cursor-pointer overflow-hidden transition-all",
            isSidebarOpen ? "h-[36px] w-[72px]" : "h-8 w-8",
          )}
        >
          <img
            src={getLogoSrc()}
            alt="Logo"
            className="h-full w-full object-contain"
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
      <div className="w-full flex-1">
        <nav className={cn("grid w-full items-start gap-1 text-sm")}>
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
    </div>
  );
}
