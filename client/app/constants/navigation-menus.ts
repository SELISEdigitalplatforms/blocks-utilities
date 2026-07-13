import { Bell, Mail, Wand2, Package, Home } from "lucide-react";
import { Menu } from "@/models/menu-models";

export const navigationMenus: Menu[] = [
  {
    id: "overview-project",
    type: "menu",
    name: "Overview",
    path: "/app/dashboard",
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
    path: "/app/project/environments",
    icon: Package,
  },
  {
    id: "email",
    type: "menu",
    name: "Email",
    path: "/app/email",
    icon: Mail,
  },
  {
    id: "notification",
    type: "menu",
    name: "Notification",
    path: "/app/notification",
    icon: Bell,
  },
  {
    id: "magic-url",
    type: "menu",
    name: "Magic URL",
    path: "/app/magic-url",
    icon: Wand2,
  },
];
