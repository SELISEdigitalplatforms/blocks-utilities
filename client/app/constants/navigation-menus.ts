import {
  Bell,
  CirclePlus,
  CreditCard,
  Home,
  Package,
  ReceiptText,
  Settings2,
  WalletCards,
  Wand2,
} from "lucide-react";
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
    id: "notification",
    type: "menu",
    name: "Notification",
    path: "/app/notification",
    icon: Bell,
  },
  {
    id: "payment",
    type: "menu",
    name: "Payments",
    path: "/app/payment",
    icon: CreditCard,
    children: [
      {
        id: "payment-create",
        type: "menu",
        name: "Create Payment",
        path: "/app/payment/create",
        icon: CirclePlus,
      },
      {
        id: "payment-list",
        type: "menu",
        name: "Payment List",
        path: "/app/payment/list",
        icon: ReceiptText,
      },
      {
        id: "payment-methods",
        type: "menu",
        name: "Saved Cards",
        path: "/app/payment/cards",
        icon: WalletCards,
      },
      {
        id: "payment-providers",
        type: "menu",
        name: "Payment Providers",
        path: "/app/payment/providers",
        icon: Settings2,
      },
    ],
  },
  {
    id: "magic-url",
    type: "menu",
    name: "Magic URL",
    path: "/app/magic-url",
    icon: Wand2,
  },
];
