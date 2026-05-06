/**
 * Centralized breadcrumb title configuration for utilities modules.
 * Maps route paths to custom display labels.
 *
 * Supports pattern matching for dynamic routes:
 * - Use :id, :itemId, etc. as placeholders for dynamic segments
 * - The pattern /email/communications/:id will match /email/communications/abc123
 *
 * Note: For dynamic segments with data from API (like template names),
 * use the useDynamicBreadcrumbLabel hook in the page component.
 */
export const BREADCRUMB_CUSTOM_TITLES: Record<string, string | null> = {
  // Email module
  "/email": "Email Templates",
  "/email/communications/:id": null, // Dynamic - label set via useDynamicBreadcrumbLabel
  "/email/communications/:id/edit": "Edit",
  "/email/usage/:id": null, // Dynamic - label set via useDynamicBreadcrumbLabel

  // Notification module
  "/notification": "Notifications",

  // Magic URL module
  "/magic-url": "Magic URL",
  "/magic-url/details/:id": null, // Dynamic - label set via useDynamicBreadcrumbLabel
};
