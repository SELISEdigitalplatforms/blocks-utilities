import { useContext, useEffect, useState } from "react";
import { PanelLeft, Menu } from "lucide-react";
import { useLocation, Link } from "react-router-dom";
import { Button } from "@/components/ui-kits/button/button";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";
import { Notification } from "@/components/notification/notification";
import { ProjectList } from "@/components/project-list/project-list";
import { UserDropdownMenu } from "@/components/user-dropdown-menu/user-dropdown-menu";
import { EnvironmentList } from "@/components/environment-list/environment-list";
import { SidebarMobileView } from "@/layouts/sidebar-mobile-view/sidebar-mobile-view";
import { BackToConsoleNavigator } from "@/components/back-to-console-navigator/back-to-console-navigator";
import { SidebarContext } from "@/contexts/dashboard-layout-provider";
import { Sheet, SheetContent, SheetTrigger } from "@/components/ui-kits/sheet/sheet";
import { Logo } from "@/components/logo";
import { BlocksAppLauncher } from "@/components/blocks-app-launcher/blocks-app-launcher";
import useIsMobile from "@/hooks/use-is-mobile";
import { cn } from "@/lib/utils";

export function ConsoleHeader() {
  const context = useContext(SidebarContext);
  const { pathname } = useLocation();
  const isMobile = useIsMobile();
  const [isScrolled, setIsScrolled] = useState(false);

  const isConsoleButtonVisible = pathname === "/profile" || pathname.startsWith("/project-overview");

  useEffect(() => {
    const handleScroll = () => {
      setIsScrolled(window.scrollY > 25);
    };
    window.addEventListener("scroll", handleScroll);
    return () => window.removeEventListener("scroll", handleScroll);
  }, []);

  return (
    <div
      className={`fixed left-0 right-0 top-0 z-40 ${isScrolled || isConsoleButtonVisible ? "border-b bg-background" : "bg-transparent"}`}
    >
      <header
        className={`mx-5 flex h-12 items-center gap-4 lg:h-[59px] ${isConsoleButtonVisible ? "sm:ml-1 sm:mr-6" : "sm:mx-10"}`}
      >
        <div className={`flex h-full w-full flex-row items-center ${isMobile && "mx-0"}`}>
          <div className="ml-2 flex h-full w-[228px] items-center">
            <Link to="/console" className="cursor-pointer">
              <Logo width={96} height={32} className="h-8 w-auto" />
            </Link>
          </div>
        </div>
        <div className="block sm:hidden">
          <Sheet>
            <SheetTrigger asChild>
              <Button variant="outline">
                <Menu className="h-5 w-5" />
              </Button>
            </SheetTrigger>
            <SheetContent
              hideClose
              side="top"
              className={`flex w-full flex-wrap items-start gap-3 ${isConsoleButtonVisible ? "justify-between" : "justify-end"}`}
            >
              {isConsoleButtonVisible && (
                <div className="min-w-fit flex-shrink">
                  <BackToConsoleNavigator />
                </div>
              )}
              <div className="flex min-w-0 flex-1 flex-wrap items-center justify-end gap-3">
                <ModeToggle />
                <Notification />
                <BlocksAppLauncher />
                <UserDropdownMenu />
              </div>
            </SheetContent>
          </Sheet>
        </div>
        <div className="hidden sm:flex sm:items-center sm:gap-4">
          {isConsoleButtonVisible && <BackToConsoleNavigator />}
          <ModeToggle />
          <Notification />
          <BlocksAppLauncher />
          <UserDropdownMenu />
        </div>
      </header>
    </div>
  );
}