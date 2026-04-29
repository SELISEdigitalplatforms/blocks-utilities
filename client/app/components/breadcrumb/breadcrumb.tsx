
import React from "react";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "../ui-kits/breadcrumb/breadcrumb";
import { Link } from "react-router-dom";
import useRoutePathSegments from "@/hooks/use-path-segments";
import { usePreviousPath } from "@/hooks/use-path-segments";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";

export interface BreadcrumbSegment {
  href: string;
  label: string;
}

const formateLabel = (label: string): string => {
  const words = label.split("-");
  const formattedWords = words.map((word) => {
    return word.charAt(0).toUpperCase() + word.slice(1);
  });
  return formattedWords.join(" ");
};

const PageBreadcrumb: React.FC<{
  breadcrumbIndex?: number;
  parentBreadcrumb?: BreadcrumbSegment;
}> = ({ breadcrumbIndex, parentBreadcrumb }) => {
  let breadcrumbs = useRoutePathSegments();
  const previousPath = usePreviousPath();

  // Auto-detect parent breadcrumb from previous path for sibling routes
  let autoParentBreadcrumb: BreadcrumbSegment | undefined;
  if (previousPath && breadcrumbs.length > 0) {
    const currentBasePath = "/" + breadcrumbs[0].href.split("/").filter(Boolean)[0];
    const previousBasePath = "/" + previousPath.split("/").filter(Boolean)[0];

    // Check if navigating from a sibling route (same parent, different child)
    if (currentBasePath === previousBasePath && previousPath !== breadcrumbs[0].href) {
      const previousSegment = previousPath.split("/").filter(Boolean).pop() || "";
      autoParentBreadcrumb = {
        href: previousPath,
        label: BREADCRUMB_CUSTOM_TITLES[previousPath] || formateLabel(previousSegment),
      };
    }
  }

  // Use explicit parent breadcrumb if provided, otherwise use auto-detected one
  const effectiveParentBreadcrumb = parentBreadcrumb || autoParentBreadcrumb;

  if (effectiveParentBreadcrumb) {
    breadcrumbs = [effectiveParentBreadcrumb, ...breadcrumbs];
  }
  if (breadcrumbIndex && breadcrumbIndex > 0) {
    // Use negative slice to get the last N segments
    breadcrumbs = breadcrumbs.slice(-breadcrumbIndex);
  }
  return (
    <Breadcrumb className="hidden md:flex">
      <BreadcrumbList>
        {breadcrumbs.map((breadcrumb, index) => (
          <React.Fragment key={breadcrumb.href}>
            <BreadcrumbItem>
              {index === breadcrumbs.length - 1 ? (
                <BreadcrumbPage className="text-low-emphasis">
                  {BREADCRUMB_CUSTOM_TITLES[breadcrumb.href] || breadcrumb.label}
                </BreadcrumbPage>
              ) : (
                <BreadcrumbLink asChild>
                  <Link to={breadcrumb.href} className="text-foreground hover:text-foreground">
                    {BREADCRUMB_CUSTOM_TITLES[breadcrumb.href] || breadcrumb.label}
                  </Link>
                </BreadcrumbLink>
              )}
            </BreadcrumbItem>
            {index < breadcrumbs.length - 1 && <BreadcrumbSeparator />}
          </React.Fragment>
        ))}
      </BreadcrumbList>
    </Breadcrumb>
  );
};

export default PageBreadcrumb;
