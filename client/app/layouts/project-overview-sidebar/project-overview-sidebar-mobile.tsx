import { useState, Fragment } from "react";
import { Settings, Users, MenuIcon, BookMinus, Package } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui-kits/sheet/sheet";
import { Separator } from "@/components/ui-kits/separator/separator";
import { MobileMenuItem } from "@/components/menus/mobile-menu-item";
import { Menu } from "@/models/menu-models";

const projectOverviewMenuItems: Menu[] = [
  {
    id: "environments",
    type: "menu" as const,
    name: "Environments",
    path: "/project-overview/environments",
    icon: Package,
  },
  {
    id: "people",
    type: "menu" as const,
    name: "People",
    path: "/project-overview/people",
    icon: Users,
  },
  {
    id: "repositories",
    type: "menu" as const,
    name: "Repositories",
    path: "/project-overview/repositories",
    icon: BookMinus,
  },
  {
    id: "settings",
    type: "menu" as const,
    name: "Project Settings",
    path: "/project-overview/settings",
    icon: Settings,
  },
];

export const ProjectOverviewSidebarMobile = () => {
  const [open, setOpen] = useState(false);

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger asChild>
        <Button variant="outline" size="icon" className="md:hidden">
          <MenuIcon className="h-5 w-5" />
          <span className="sr-only">Toggle project overview menu</span>
        </Button>
      </SheetTrigger>
      <SheetContent side="left" className="w-full p-0" aria-describedby={undefined}>
        <SheetHeader className="border-b px-4 py-5">
          <SheetTitle>Project Overview</SheetTitle>
        </SheetHeader>
        <Separator />
        <nav className="grid gap-2 p-4">
          {projectOverviewMenuItems.map((item) => (
            <Fragment key={item.id}>
              {item.type === "menu" && (
                <MobileMenuItem menu={item} onClick={() => setOpen(false)} />
              )}
            </Fragment>
          ))}
        </nav>
      </SheetContent>
    </Sheet>
  );
};
