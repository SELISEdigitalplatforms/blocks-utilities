import { Bell, Mail, Wand2, LayoutGrid } from "lucide-react";
import { Menu } from "@/models/menu-models";

export const navigationMenus: Menu[] = [
  {
    id: "environments",
    type: "menu",
    name: "Environments",
    path: "/project-overview/environments",
    icon: LayoutGrid,
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
