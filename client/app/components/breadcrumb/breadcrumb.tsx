
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
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";

const PageBreadcrumb: React.FC<{ breadcrumbIndex?: number }> = ({ breadcrumbIndex }) => {
  let breadcrumbs = useRoutePathSegments();
  if (breadcrumbIndex && breadcrumbIndex > 0) {
    breadcrumbs = breadcrumbs.slice(breadcrumbIndex - 1);
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
