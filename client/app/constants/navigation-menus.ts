import { Menu } from "@/models/menu-models";
import { Home, Package, Users, BookMinus, Settings, Shield, Key, ShieldCheck, ScanFace, Lock, Zap, Gauge } from "lucide-react";

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
    id: "people",
    type: "menu",
    name: "People",
    path: "/project-overview/people",
    icon: Users,
  },
  {
    id: "repositories",
    type: "menu",
    name: "Repositories",
    path: "/project-overview/repositories",
    icon: BookMinus,
  },
  {
    id: "settings",
    type: "menu",
    name: "Project Settings",
    path: "/project-overview/settings",
    icon: Settings,
  },
  {
    type: "separator",
    id: "separator-identity",
  },
  {
    id: "service-identity__authentication",
    type: "menu",
    name: "IDP",
    path: "/services/authentication",
    icon: Key,
  },
  // {
  //   id: "service-identity__authorization",
  //   type: "menu",
  //   name: "Access Manager",
  //   path: "/services/iam",
  //   icon: Shield,
  // },
  // {
  //   id: "service-identity__mfa",
  //   type: "menu",
  //   name: "MFA",
  //   path: "/services/mfa",
  //   icon: ShieldCheck,
  // },
  // {
  //   id: "service-identity__captcha",
  //   type: "menu",
  //   name: "Captcha",
  //   path: "/services/captcha",
  //   icon: ScanFace,
  // },
  {
    id: "service-identity__api-settings",
    type: "menu",
    name: "API Settings",
    path: "/services/api-settings",
    icon: Settings,
  },
  {
    id: "service-identity__secret-management",
    type: "menu",
    name: "Secrets & Configs",
    path: "/services/secret-management",
    icon: Lock,
  },
  {
    id: "service-identity__lmt",
    type: "menu",
    name: "LMT",
    path: "/services/lmt",
    icon: Zap,
  },
];
