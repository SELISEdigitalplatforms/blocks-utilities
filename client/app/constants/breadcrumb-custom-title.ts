import { tStatic } from "@seliseblocks/genesis-os";

type BreadcrumbRouteConfig = {
  title?: string;
  skip?: boolean;
  dynamic?: boolean;
  href?: string;
};

export const getBreadcrumbRoutes = (): Record<string, BreadcrumbRouteConfig> => ({
  "/notification": {
    title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.NOTIFICATIONS"),
  },
  "/app/:itemId/notification": {
    title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.NOTIFICATIONS"),
  },
  "/payment": {
    title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.PAYMENTS"),
  },
  "/app/:itemId/payment": {
    title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.PAYMENTS"),
  },
  "/payment/list": {
    title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.PAYMENT_LIST"),
  },
  "/app/:itemId/payment/list": {
    title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.PAYMENT_LIST"),
  },
  "/payment/create": {
    title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.CREATE_PAYMENT"),
  },
  "/app/:itemId/payment/create": {
    title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.CREATE_PAYMENT"),
  },
  "/payment/cards": {
    title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.SAVED_CARDS"),
  },
  "/app/:itemId/payment/cards": {
    title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.SAVED_CARDS"),
  },
  "/payment/result": {
    title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.PAYMENT_RESULT"),
  },
  "/app/:itemId/payment/result": {
    title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.PAYMENT_RESULT"),
  },
  "/sms": { title: "SMS" },
  "/app/:itemId/sms": { title: "SMS" },
  "/sms/send": { title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.SEND_SMS") },
  "/app/:itemId/sms/send": { title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.SEND_SMS") },
  "/sms/templates": { title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.TEMPLATES") },
  "/app/:itemId/sms/templates": { title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.TEMPLATES") },
  "/sms/provider": { title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.PROVIDER_SETTINGS") },
  "/app/:itemId/sms/provider": { title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.PROVIDER_SETTINGS") },
  "/magic-url": {
    title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.MAGIC_URL"),
  },
  "/app/:itemId/magic-url": {
    title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.MAGIC_URL"),
  },
  "/magic-url/details": {
    title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.LIST"),
    href: "/magic-url",
  },
  "/app/:itemId/magic-url/details": {
    title: tStatic("utilities.shared", "BREADCRUMB_CUSTOM_TITLE.LIST"),
    href: "/app/:itemId/magic-url",
  },
  "/magic-url/details/:id": {
    dynamic: true,
  },
  "/app/:itemId/magic-url/details/:id": {
    dynamic: true,
  },
});

const BREADCRUMB_CUSTOM_TITLES: Record<string, string | null> = {};
const BREADCRUMB_HREF_OVERRIDES: Record<string, string> = {};
const BREADCRUMB_SKIP_PATHS: string[] = [];

for (const [path, config] of Object.entries(getBreadcrumbRoutes())) {
  if (config.title !== undefined) {
    BREADCRUMB_CUSTOM_TITLES[path] = config.title;
  } else if (config.dynamic) {
    BREADCRUMB_CUSTOM_TITLES[path] = null;
  }
  if (config.skip) {
    BREADCRUMB_SKIP_PATHS.push(path);
  }
  if (config.href) {
    BREADCRUMB_HREF_OVERRIDES[path] = config.href;
  }
}

export {
  BREADCRUMB_CUSTOM_TITLES,
  BREADCRUMB_HREF_OVERRIDES,
  BREADCRUMB_SKIP_PATHS,
};
