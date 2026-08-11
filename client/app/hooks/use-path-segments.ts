import { useLocation } from "react-router";
import { useRef, useEffect, useState } from "react";
import {
  BREADCRUMB_CUSTOM_TITLES,
  BREADCRUMB_HREF_OVERRIDES,
  BREADCRUMB_SKIP_PATHS,
} from "@/constants/breadcrumb-custom-title";
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
  const processedBreadcrumbs = breadcrumbs.map((breadcrumb) => {
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
  });

  const linkedBreadcrumbs = processedBreadcrumbs.map((breadcrumb) => {
    for (const [pattern, target] of Object.entries(
      BREADCRUMB_HREF_OVERRIDES,
    )) {
      if (
        pattern === breadcrumb.href ||
        matchDynamicPath(pattern, breadcrumb.href)
      ) {
        return {
          ...breadcrumb,
          href: resolveDynamicPath(pattern, target, breadcrumb.href),
        };
      }
    }

    return breadcrumb;
  });

  return linkedBreadcrumbs.filter(
    (breadcrumb) =>
      !BREADCRUMB_SKIP_PATHS.some(
        (pattern) =>
          pattern === breadcrumb.href ||
          matchDynamicPath(pattern, breadcrumb.href),
      ),
  );
};

// Match paths like /services/glossary/:itemId against /services/glossary/abc123.
const matchDynamicPath = (pattern: string, actual: string): boolean => {
  const patternParts = pattern.split("/").filter(Boolean);
  const actualParts = actual.split("/").filter(Boolean);
  if (patternParts.length !== actualParts.length) return false;
  return patternParts.every((part, i) => {
    // Support :paramName placeholders in pattern (e.g., :id, :itemId)
    if (part.startsWith(":")) return true;
    return part === actualParts[i];
  });
};

const resolveDynamicPath = (
  pattern: string,
  target: string,
  actual: string,
): string => {
  const patternParts = pattern.split("/").filter(Boolean);
  const actualParts = actual.split("/").filter(Boolean);
  const parameterValues = new Map<string, string>();

  patternParts.forEach((part, index) => {
    if (part.startsWith(":")) {
      parameterValues.set(part, actualParts[index]);
    }
  });

  const resolvedParts = target
    .split("/")
    .filter(Boolean)
    .map((part) => parameterValues.get(part) ?? part);

  return `/${resolvedParts.join("/")}`;
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
