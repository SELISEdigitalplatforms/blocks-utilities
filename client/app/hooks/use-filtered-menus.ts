import { useMemo } from "react";
import { useLocation } from "react-router-dom";
import { Menu } from "@/models/menu-models";

export function useFilteredMenus(menus: Menu[]): Menu[] {
  const { pathname } = useLocation();

  return useMemo(() => {
    const blockedMenu = import.meta.env.BLOCKS_BLOCKED_MENU || "[]";
    let parsedBlockedMenu: string[] = [];
    const isProjectOverviewRoute = pathname.startsWith("/project-overview");
    const projectOverviewMenuIds = new Set([
      "environments",
      "people",
      "repositories",
      "settings",
    ]);
    const nonProjectMenuIds = new Set([
      "overview-project",
      "service-identity__authentication",
      "service-identity__authorization",
      "service-identity__mfa",
      "service-identity__captcha",
      "service-identity__api-settings",
      "service-identity__secret-management",
      "service-identity__lmt",
            "service-identity__apps",

    ]);

    try {
      parsedBlockedMenu = JSON.parse(blockedMenu) as string[];
    } catch (error) {
      console.error("Failed to parse BLOCKS_BLOCKED_MENU:", error);
    }

    const filteredMenus = menus.filter((item) => {
      if (item.type === "separator") return true;
      if (item.disabled) return false;
      // Hide project menus when NOT on /project-overview
      if (!isProjectOverviewRoute && projectOverviewMenuIds.has(item.id)) return false;
      // Hide non-project menus when ON /project-overview
      if (isProjectOverviewRoute && nonProjectMenuIds.has(item.id)) return false;
      return !parsedBlockedMenu.includes(item.id);
    });

    const result = filteredMenus.filter((item, index) => {
      if (item.type !== "separator") return true;

      const previousItem = filteredMenus[index - 1];
      const nextItem = filteredMenus[index + 1];
      const separatorId = (item as any).id;

      // Hide separator-overview on project overview routes (Overview is hidden there)
      if (separatorId === "separator-overview" && isProjectOverviewRoute) {
        return false;
      }

      // Keep separator-overview if Overview is before it and not on project route
      if (separatorId === "separator-overview" && previousItem?.type !== "separator" && !isProjectOverviewRoute) {
        return true;
      }

      if (!previousItem || !nextItem) return false;
      if (previousItem.type === "separator" || nextItem.type === "separator") return false;

      return true;
    });

    return result;
  }, [menus, pathname]);
}