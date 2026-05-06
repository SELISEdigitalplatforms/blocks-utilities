import { useLocation } from "react-router-dom";
import { useRef, useEffect, useState } from "react";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { useBreadcrumbLabels } from "@/contexts/breadcrumb-context";

const useRoutePathSegments = () => {
  const { pathname } = useLocation();
  const pathArray = pathname.split("/").filter((path) => path);
  const dynamicLabels = useBreadcrumbLabels();

  const breadcrumbs = pathArray.map((path, index) => {
    const href = "/" + pathArray.slice(0, index + 1).join("/");
    return {
      href,
      label: formateLabel(path),
    };
  });

  // Apply custom titles with pattern matching for dynamic segments
  // Also handle special cases for utilities modules
  return breadcrumbs
    .map((breadcrumb) => {
      // Priority 1: Dynamic labels from context (set by pages for dynamic content)
      if (dynamicLabels[breadcrumb.href]) {
        return {
          ...breadcrumb,
          label: dynamicLabels[breadcrumb.href],
        };
      }

      // Priority 2: Direct match in custom titles
      if (BREADCRUMB_CUSTOM_TITLES[breadcrumb.href] !== undefined) {
        return {
          ...breadcrumb,
          label: BREADCRUMB_CUSTOM_TITLES[breadcrumb.href] ?? breadcrumb.label,
        };
      }
      // Pattern match: check if any pattern key matches this href
      for (const [pattern, title] of Object.entries(BREADCRUMB_CUSTOM_TITLES)) {
        if (pattern !== breadcrumb.href && matchDynamicPath(pattern, breadcrumb.href)) {
          return { ...breadcrumb, label: title ?? breadcrumb.label };
        }
      }
      return breadcrumb;
    })
    .filter((breadcrumb) => {
      // Filter out segments that should be hidden (null in config)
      // Also hide intermediate segments for utilities routes
      return !shouldHideSegment(breadcrumb.href, pathname);
    });
};

// Match paths like /services/glossary/:itemId against /services/glossary/abc123
// or /email/communications/:id against /email/communications/abc123
const matchDynamicPath = (pattern: string, actual: string): boolean => {
  const patternParts = pattern.split("/").filter(Boolean);
  const actualParts = actual.split("/").filter(Boolean);
  if (patternParts.length !== actualParts.length) return false;
  return patternParts.every((part, i) => {
    // Support :paramName placeholders in pattern (e.g., :id, :itemId)
    if (part.startsWith(":")) return true;
    // UUID/itemId pattern: alphanumeric with dashes (min 5 chars to avoid false positives)
    if (/^[a-zA-Z0-9-]{5,}$/.test(actualParts[i])) return true;
    return part === actualParts[i];
  });
};

// Check if a segment should be hidden from breadcrumbs
const shouldHideSegment = (href: string, pathname: string): boolean => {
  // Check if this href or a pattern matching it is set to null
  if (BREADCRUMB_CUSTOM_TITLES[href] === null) {
    return true;
  }

  // Check patterns
  for (const [pattern, title] of Object.entries(BREADCRUMB_CUSTOM_TITLES)) {
    if (title === null && matchDynamicPath(pattern, href)) {
      return true;
    }
  }

  // Special handling for utilities modules - hide intermediate routes
  // For paths like /email/communications/:id, hide /email/communications
  const utilitiesPatterns = [
    { parent: "/email/communications", childPattern: "/email/communications/" },
    { parent: "/email/usage", childPattern: "/email/usage/" },
    { parent: "/magic-url/details", childPattern: "/magic-url/details/" },
  ];

  for (const { parent, childPattern } of utilitiesPatterns) {
    if (pathname.includes(childPattern) && href === parent) {
      return true;
    }
  }

  return false;
};

const formateLabel = (label: string): string => {
  const words = label.split("-");
  const formattedWords = words.map((word) => {
    return word.charAt(0).toUpperCase() + word.slice(1);
  });
  return formattedWords.join(" ");
};

export const usePreviousPath = () => {
  const location = useLocation();
  const [previousPath, setPreviousPath] = useState<string | null>(null);
  const previousPathRef = useRef<string | null>(null);

  useEffect(() => {
    const currentPath = location.pathname;
    if (previousPathRef.current !== null && previousPathRef.current !== currentPath) {
      setPreviousPath(previousPathRef.current);
    }
    previousPathRef.current = currentPath;
  }, [location.pathname]);

  return previousPath;
};

export default useRoutePathSegments;
