import { useMemo } from "react";
import { ChevronRight } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { Badge } from "@/components/ui-kits/badge/badge";
import { cn } from "@/lib/utils";
import { Menu } from "@/models/menu-models";

type MenuItemType = Extract<Menu, { type: "menu" }>;

function ChildMenuItem({ menu }: { menu: MenuItemType }) {
  const { pathname } = useLocation();
  const isActiveMenu = pathname.startsWith(menu.path);

  return (
    <Link
      to={menu.path}
      className={cn(
        "flex h-10 items-center px-4 py-1.5 text-base transition-colors hover:text-[hsl(var(--high-emphasis))]",
        isActiveMenu && "!text-primary",
        menu.disabled && "pointer-events-none cursor-not-allowed opacity-50",
      )}
    >
      {menu.icon ? <menu.icon className="mr-2 h-5 w-5" /> : null}
      <span>{menu.name}</span>
    </Link>
  );
}

export function DesktopMenuItem({ menu, isSidebarOpen }: { menu: MenuItemType; isSidebarOpen: boolean }) {
  const { pathname } = useLocation();

  const isActiveMenu = useMemo(() => {
    const allPaths = [menu.path];
    if (menu.children) {
      menu.children.forEach((child) => {
        if (child.type === "menu") allPaths.push(child.path);
      });
    }
    return allPaths.some((item) => pathname.startsWith(item));
  }, [menu.children, menu.path, pathname]);

  const hasChildren = Boolean(menu.children?.length);

  const baseClasses = cn(
    "relative flex h-10 cursor-pointer items-center gap-3 px-4 py-1.5 text-base text-[hsl(var(--low-emphasis))] hover:text-[hsl(var(--high-emphasis))]",
    isActiveMenu && "!text-primary",
  );

  if (!hasChildren) {
    return (
      <div className={cn(baseClasses, "group justify-between")}>
        <Link to={menu.path} className={cn("flex items-center gap-3", menu.disabled && "pointer-events-none opacity-50")}>
          {menu.icon ? <menu.icon className="h-5 w-5" /> : null}
          {isSidebarOpen ? (
            <span className="relative">
              {menu.name}
              {menu.badge ? (
                <Badge
                  variant="secondary"
                  className="absolute -top-2 left-full ml-1 h-4 px-1 text-[9px] font-semibold uppercase text-primary"
                >
                  {menu.badge}
                </Badge>
              ) : null}
            </span>
          ) : null}
        </Link>
        {!isSidebarOpen ? (
          <div className="pointer-events-none absolute left-full top-0 z-20 ml-2 min-w-max whitespace-nowrap rounded bg-gray-300 px-2 py-1 text-xs text-primary opacity-0 transition-opacity group-hover:opacity-100">
            {menu.name}
          </div>
        ) : null}
        {isActiveMenu ? <div className="absolute right-0 top-2.5 h-5 w-1 rounded-lg bg-primary" /> : null}
      </div>
    );
  }

  return (
    <div className={cn(baseClasses, "group")}>
      <div className="flex items-center gap-3">
        {menu.icon ? <menu.icon className="h-5 w-5" /> : null}
        {isSidebarOpen ? (
          <span className="relative">
            {menu.name}
            {menu.badge ? (
              <Badge
                variant="outline"
                className="absolute -top-2 left-full ml-1 h-4 px-1 text-[9px] font-semibold uppercase text-primary"
              >
                {menu.badge}
              </Badge>
            ) : null}
          </span>
        ) : null}
      </div>
      {!isSidebarOpen ? (
        <div className="pointer-events-none absolute left-1/2 top-full z-20 mt-2 -translate-x-1/2 rounded bg-gray-300 px-2 py-1 text-xs text-primary opacity-0 transition-opacity group-hover:opacity-100">
          <span className="whitespace-nowrap">{menu.name}</span>
        </div>
      ) : null}
      {isSidebarOpen ? <ChevronRight className="ml-auto h-4 w-4" /> : null}
      {isActiveMenu ? <div className="absolute right-0 top-2.5 h-5 w-1 rounded-lg bg-primary" /> : null}

      <div className="absolute left-full top-0 z-10 hidden w-64 flex-col rounded-sm border bg-background py-2 group-hover:flex group-hover:text-[hsl(var(--low-emphasis))]">
        {menu.children
          ?.filter((subMenu): subMenu is MenuItemType => subMenu.type === "menu" && !subMenu.disabled)
          .map((subMenu) => <ChildMenuItem key={subMenu.id} menu={subMenu} />)}
      </div>
      <div className="absolute left-full top-0 hidden h-full w-1 bg-transparent group-hover:block" />
    </div>
  );
}