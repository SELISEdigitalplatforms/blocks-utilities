import { Package } from "lucide-react";
import { DesktopMenuItem } from "@/components/menus/desktop-menu-item";
import { Menu } from "@/models/menu-models";

const projectOverviewMenuItems: Menu[] = [
  {
    id: "environments",
    type: "menu" as const,
    name: "Environments",
    path: "/project-overview/environments",
    icon: Package,
  },
];

export const ProjectOverviewSidebarDesktop = () => {
  return (
    <aside className="sticky top-0 hidden h-full w-60 shrink-0 border-r bg-background md:block">
      <nav className="grid w-full items-start gap-1 text-sm">
        {projectOverviewMenuItems
          .filter((item) => item.type === "menu")
          .map((item) => (
            <DesktopMenuItem key={item.id} menu={item} isSidebarOpen={true} />
          ))}
      </nav>
    </aside>
  );
};
