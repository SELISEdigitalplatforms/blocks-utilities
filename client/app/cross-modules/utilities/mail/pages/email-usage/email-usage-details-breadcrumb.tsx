import React from "react";
import { useNavigate } from "react-router-dom";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui-kits/breadcrumb/breadcrumb";
import { Link } from "react-router-dom";

interface EmailUsageDetailsBreadcrumbProps {
  id: string;
  isInbound?: boolean;
}

/**
 * Custom breadcrumb for email usage details.
 * This page uses a query param-based navigation (/email?emailAnalytics=...)
 * instead of the standard route structure, so it needs a custom breadcrumb.
 */
export const EmailUsageDetailsBreadcrumb = ({
  id,
  isInbound,
}: EmailUsageDetailsBreadcrumbProps) => {
  const navigate = useNavigate();
  // Navigate back to the appropriate email analytics tab
  const backLink = isInbound
    ? "/email?emailAnalytics=Inbox"
    : "/email?emailAnalytics=Outgoingmails";

  return (
    <Breadcrumb>
      <BreadcrumbList>
        <BreadcrumbItem>
          <BreadcrumbLink asChild>
            <Link
              to={backLink}
              className="text-foreground hover:text-foreground"
              onClick={(e) => {
                // Use programmatic navigation to preserve query params
                e.preventDefault();
                navigate(backLink);
              }}
            >
              Email
            </Link>
          </BreadcrumbLink>
        </BreadcrumbItem>
        <BreadcrumbSeparator />
        <BreadcrumbItem>
          <BreadcrumbPage className="text-low-emphasis">
            {/* Truncate long IDs for display */}
            {id.length > 20 ? `${id.slice(0, 20)}...` : id}
          </BreadcrumbPage>
        </BreadcrumbItem>
      </BreadcrumbList>
    </Breadcrumb>
  );
};
