import { describe, expect, it } from "vitest";
import { ModuleName } from "./modules.constants";
import {
  BREADCRUMB_ROUTES,
  BREADCRUMB_CUSTOM_TITLES,
  BREADCRUMB_HREF_OVERRIDES,
} from "./breadcrumb-custom-title";
import { environmentOptions } from "./environment-options";
import { navigationMenus } from "./navigation-menus";
import { BLOCKS_PRODUCTS } from "./blocks-products";
import {
  REGISTER_SERVICE_TYPE,
  REGISTER_SERVICE_TYPES,
  REGISTER_SERVICE_ENVIRONMENTS,
  LOG_LEVELS,
  SERVICE_STATUS,
  TRACE_STATUS,
} from "@/cross-modules/identifier/constants";

describe("ModuleName enum", () => {
  it("maps known module names to their numeric ids", () => {
    expect(ModuleName.Cloud).toBe(1);
    expect(ModuleName.DataGateway).toBe(11);
    expect(ModuleName[7]).toBe("LMT");
  });
});

describe("breadcrumb constants", () => {
  it("derives custom titles from routes that declare a title", () => {
    expect(BREADCRUMB_ROUTES["/magic-url"].title).toBe("Magic URL");
    expect(BREADCRUMB_CUSTOM_TITLES["/magic-url"]).toBe("Magic URL");
  });

  it("marks dynamic routes with a null title", () => {
    expect(BREADCRUMB_CUSTOM_TITLES["/magic-url/details/:id"]).toBeNull();
  });

  it("maps the Magic URL list breadcrumb back to the list route", () => {
    expect(BREADCRUMB_CUSTOM_TITLES["/magic-url/details"]).toBe("List");
    expect(BREADCRUMB_HREF_OVERRIDES["/magic-url/details"]).toBe(
      "/magic-url",
    );
  });
});

describe("environmentOptions", () => {
  it("lists every environment with contiguous indexes", () => {
    expect(environmentOptions).toHaveLength(8);
    environmentOptions.forEach((opt, i) => expect(opt.index).toBe(i));
    expect(environmentOptions.at(-1)?.value).toBe("prod");
  });
});

describe("navigationMenus", () => {
  it("includes the overview entry", () => {
    const overview = navigationMenus.find((m) => m.id === "overview-project");
    expect(overview?.name).toBe("Overview");
  });

  it("does not expose email or notification links", () => {
    const hiddenMenuIds = new Set(["email", "notification"]);

    expect(navigationMenus.some((menu) => hiddenMenuIds.has(menu.id))).toBe(false);
  });

  /**
   * The Discounts route has been wired in the router for some time, but nothing in the sidebar
   * pointed at it, so the page was reachable only by typing its URL.
   */
  describe("the Subscriptions group", () => {
    const children = () => {
      const group = navigationMenus.find((menu) => menu.id === "subscription");
      if (group?.type !== "menu" || !group.children) {
        throw new Error("the Subscriptions group is a menu with children");
      }
      return group.children;
    };

    const ids = () => children().map((child) => child.id);

    it("offers Discounts, pointing at the route the router declares", () => {
      const discounts = children().find((child) => child.id === "subscription-discounts");

      expect(discounts?.type).toBe("menu");
      expect(discounts?.type === "menu" && discounts.name).toBe("Discounts");
      expect(discounts?.type === "menu" && discounts.path).toBe("/app/subscription/discounts");
    });

    it("puts Discounts immediately after Plans", () => {
      const order = ids();

      expect(order.indexOf("subscription-discounts")).toBe(
        order.indexOf("subscription-plans") + 1,
      );
    });

    it("gives Discounts an icon, like every other entry in the group", () => {
      const discounts = children().find((child) => child.id === "subscription-discounts");

      expect(discounts?.type === "menu" && discounts.icon).toBeTruthy();
    });

    /**
     * The organization scope is carried onto sidebar links by a prefix test on `/app/subscription`
     * in `withSubscriptionOrganizationScope`. An entry authored under any other prefix would render
     * a link that silently drops the organization in view, so this holds for the whole group rather
     * than for Discounts alone.
     */
    it("authors every entry under the prefix the organization scope matches", () => {
      for (const child of children()) {
        if (child.type !== "menu") continue;
        expect(child.path.startsWith("/app/subscription")).toBe(true);
      }
    });
  });
});

describe("BLOCKS_PRODUCTS", () => {
  it("exposes products with required display fields", () => {
    expect(BLOCKS_PRODUCTS.length).toBeGreaterThan(0);
    const os = BLOCKS_PRODUCTS.find((p) => p.name === "blocks-os");
    expect(os?.appName).toBe("blocks OS");
    expect(Array.isArray(os?.featureChips)).toBe(true);
  });
});

describe("identifier constants", () => {
  it("defines service types and labels", () => {
    expect(REGISTER_SERVICE_TYPE.Api).toBe(1);
    expect(REGISTER_SERVICE_TYPES).toHaveLength(3);
    expect(REGISTER_SERVICE_ENVIRONMENTS.map((e) => e.value)).toContain("prod");
  });

  it("defines log level, service and trace status tables", () => {
    expect(LOG_LEVELS.map((l) => l.value)).toContain("error");
    expect(SERVICE_STATUS.map((s) => s.value)).toContain("active");
    expect(TRACE_STATUS.map((t) => t.value)).toContain("timeout");
  });
});
