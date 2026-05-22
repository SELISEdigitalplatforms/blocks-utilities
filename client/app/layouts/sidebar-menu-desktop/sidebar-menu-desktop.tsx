import { Fragment, useContext } from "react";
import { PanelLeft } from "lucide-react";
import { Link } from "react-router-dom";
import { DesktopMenuItem } from "@/components/menus/desktop-menu-item";
import { Logo } from "@/components/logo";
import { Button } from "@/components/ui-kits/button/button";
import { Separator } from "@/components/ui-kits/separator/separator";
import { navigationMenus } from "@/constants/navigation-menus";
import { SidebarContext } from "@/contexts/dashboard-layout-provider";
import { useFilteredMenus } from "@/hooks/use-filtered-menus";
import { cn } from "@/lib/utils";

export function SidebarMenuDesktop() {
  const { isSidebarOpen, toggleSidebar } = useContext(SidebarContext);
  const allowedMenu = useFilteredMenus(navigationMenus);

  return (
    <div
      className={`hidden h-[calc(100vh)] flex-col border-r bg-background md:flex ${isSidebarOpen ? "min-w-60" : "w-14"}`}
    >
      <div className="flex h-[60px] shrink-0 items-center justify-between border-b bg-background px-3">
        <Link
          to="/console"
          className={cn(
            "relative inline-block cursor-pointer overflow-hidden",
            isSidebarOpen ? "h-[36px] w-[72px]" : "h-8 w-8",
          )}
        >
          <Logo
            variant={isSidebarOpen ? "logo" : "icon"}
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
