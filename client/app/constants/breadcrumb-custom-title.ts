type BreadcrumbRouteConfig = {
  title?: string;
  skip?: boolean;
  dynamic?: boolean;
};

export const BREADCRUMB_ROUTES: Record<string, BreadcrumbRouteConfig> = {
  "/email": {
    title: "Email Templates",
  },
  "/email/communications": {
    skip: true,
  },
  "/email/communications/:id": {
    dynamic: true,
  },
  "/email/communications/:id/edit": {
    title: "Edit",
  },
  "/email/usage/:id": {
    dynamic: true,
  },
  "/notification": {
    title: "Notifications",
  },
  "/app/:itemId/notification": {
    title: "Notifications",
  },
  "/payment": {
    title: "Payments",
  },
  "/app/:itemId/payment": {
    title: "Payments",
  },
  "/payment/list": {
    title: "Payment List",
  },
  "/app/:itemId/payment/list": {
    title: "Payment List",
  },
  "/payment/create": {
    title: "Create Payment",
  },
  "/app/:itemId/payment/create": {
    title: "Create Payment",
  },
  "/payment/cards": {
    title: "Saved Cards",
  },
  "/app/:itemId/payment/cards": {
    title: "Saved Cards",
  },
  "/payment/result": {
    title: "Payment Result",
  },
  "/app/:itemId/payment/result": {
    title: "Payment Result",
  },
  "/magic-url": {
    title: "Magic URL",
  },
  "/magic-url/details": {
    skip: true,
  },
  "/magic-url/details/:id": {
    dynamic: true,
  },
};

const BREADCRUMB_CUSTOM_TITLES: Record<string, string | null> = {};
const BREADCRUMB_SKIP_PATHS: string[] = [];

for (const [path, config] of Object.entries(BREADCRUMB_ROUTES)) {
  if (config.title !== undefined) {
    BREADCRUMB_CUSTOM_TITLES[path] = config.title;
  } else if (config.dynamic) {
    BREADCRUMB_CUSTOM_TITLES[path] = null;
  }
  if (config.skip) {
    BREADCRUMB_SKIP_PATHS.push(path);
  }
}

export { BREADCRUMB_CUSTOM_TITLES, BREADCRUMB_SKIP_PATHS };
