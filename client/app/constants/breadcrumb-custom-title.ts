/**
 * Centralized breadcrumb title configuration for utilities modules.
 * Maps route paths to custom display labels.
 * Use null to hide a segment from breadcrumbs.
 *
 * Supports pattern matching for dynamic routes:
 * - Use :id, :itemId, etc. as placeholders for dynamic segments
 * - The pattern /email/communications/:id will match /email/communications/abc123
 */
export const BREADCRUMB_CUSTOM_TITLES: Record<string, string | null> = {
  // Email module
  "/email": "Email",
  "/email/communications/:id": null, // This is just a segment marker, not a full route
  "/email/communications/:id/edit": "Edit Template",
  "/email/usage/:id": null, // This is just a segment marker

  // Notification module
  "/notification": "Notifications",

  // Magic URL module
  "/magic-url": "Magic URL",
  "/magic-url/details/:id": null, // This is just a segment marker
};
