import { Bell, Mail, Wand2, Package, Home } from "lucide-react";
import { Menu } from "@/models/menu-models";

export const navigationMenus: Menu[] = [
  {
    id: "overview-project",
    type: "menu",
    name: "Overview",
    path: "/dashboard",
    icon: Home,
  },
  {
    type: "separator",
    id: "separator-overview",
  },
  {
    id: "environments",
    type: "menu",
    name: "Environments",
    path: "/project-overview/environments",
    icon: Package,
  },
  {
    id: "email",
    type: "menu",
    name: "Email",
    path: "/email",
    icon: Mail,
  },
  {
    id: "notification",
    type: "menu",
    name: "Notification",
    path: "/notification",
    icon: Bell,
  },
  {
    id: "magic-url",
    type: "menu",
    name: "Magic URL",
    path: "/magic-url",
    icon: Wand2,
  },
];
