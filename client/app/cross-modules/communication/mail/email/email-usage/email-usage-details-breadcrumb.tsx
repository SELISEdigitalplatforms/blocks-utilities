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

interface EmailUsageDetailsBreadcrumbProps {
  id: string;
  isInbound?: boolean;
}

export const EmailUsageDetailsBreadcrumb = ({
  id,
  isInbound,
}: EmailUsageDetailsBreadcrumbProps) => {
  const navigate = useNavigate();
  const backLink = isInbound
    ? "/utilities/email?emailAnalytics=Inbox"
    : "/utilities/email?emailAnalytics=Outgoingmails";

  return (
    <Breadcrumb>
      <BreadcrumbList>
        <BreadcrumbItem>
          <BreadcrumbLink asChild>
            <button onClick={() => navigate(backLink)}>Email</button>
          </BreadcrumbLink>
        </BreadcrumbItem>
        <BreadcrumbSeparator />
        <BreadcrumbItem>
          <BreadcrumbPage>{id}</BreadcrumbPage>
        </BreadcrumbItem>
      </BreadcrumbList>
    </Breadcrumb>
  );
};
