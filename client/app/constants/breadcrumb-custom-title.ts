/**
 * Centralized breadcrumb title configuration for utilities modules.
 * Maps route paths to custom display labels.
 * Use null to hide a segment from breadcrumbs.
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
  "/email/communications": null, // Hide intermediate route
  "/email/communications/:id": null, // Dynamic - label set via useDynamicBreadcrumbLabel
  "/email/communications/:id/edit": "Edit Template",
  "/email/usage": null, // Hide intermediate route
  "/email/usage/:id": null, // Dynamic - label set via useDynamicBreadcrumbLabel

  // Notification module
  "/notification": "Notifications",

  // Magic URL module
  "/magic-url": "Magic URL",
  "/magic-url/details": null, // Hide intermediate route
  // Note: /magic-url/details/:id is not set to null because we need to show it with dynamic label
};
