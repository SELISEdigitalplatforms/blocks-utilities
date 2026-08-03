
import React from "react";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "../ui-kits/breadcrumb/breadcrumb";
import { Link } from "react-router";
import useRoutePathSegments from "@/hooks/use-path-segments";

export interface BreadcrumbSegment {
  href: string;
  label: string;
}

const PageBreadcrumb: React.FC<{
  breadcrumbIndex?: number;
  parentBreadcrumb?: BreadcrumbSegment;
}> = ({ breadcrumbIndex, parentBreadcrumb }) => {
  let breadcrumbs = useRoutePathSegments();

  // Prepend parent breadcrumb if provided
  if (parentBreadcrumb) {
    breadcrumbs = [parentBreadcrumb, ...breadcrumbs];
  }

  // Slice to show only the last N breadcrumbs
  if (breadcrumbIndex && breadcrumbIndex > 0) {
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
                  {breadcrumb.label}
                </BreadcrumbPage>
              ) : (
                <BreadcrumbLink asChild>
                  <Link to={breadcrumb.href} className="text-foreground hover:text-foreground">
                    {breadcrumb.label}
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
