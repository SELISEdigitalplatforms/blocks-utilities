/**
 * Parses a resource string to extract the resource type
 * @param resource - Resource string in format "blocks-identifier-api::people::invite"
 * @returns The extracted resource type in uppercase (e.g., "PEOPLE")
 */
export function parseResourceType(resource: string): string {
  if (!resource) return "";

  const parts = resource.split("::");
  if (parts.length < 2) return resource.toUpperCase();

  // Extract the middle part (e.g., "people" from "blocks-identifier-api::people::invite")
  const resourceType = parts[1];
  return resourceType.toUpperCase();
}

/**
 * Formats resource type for display
 * @param resource - Resource string in format "blocks-identifier-api::people::invite"
 * @returns Formatted display name (e.g., "People")
 */
export function formatResourceName(resource: string): string {
  const type = parseResourceType(resource);
  if (!type) return "Unknown";

  // Capitalize first letter, rest lowercase
  return type.charAt(0) + type.slice(1).toLowerCase();
}
