import React from "react";
import PageBreadcrumb, { BreadcrumbSegment } from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { LogsViewer } from "@blocks-lmt/components";

interface EmailLogsProps {
  parentBreadcrumb?: BreadcrumbSegment;
}

export function EmailLogs({ parentBreadcrumb }: EmailLogsProps = {}) {
  BREADCRUMB_CUSTOM_TITLES["/email"] = "Email";
  BREADCRUMB_CUSTOM_TITLES["/email/logs"] = "Logs";
  return (
    <div className="flex flex-col gap-6 p-6">
      <PageBreadcrumb breadcrumbIndex={2} parentBreadcrumb={parentBreadcrumb} />
      <LogsViewer
        services={[
          {
            id: "blocks-communication-api",
            label: "Api",
            serviceName: "blocks-communication-api",
          },
          {
            id: "blocks-communication-worker",
            label: "Worker",
            serviceName: "blocks-communication-worker",
          },
        ]}
        predefinedQueries={[
          "Has anyone faced any email issues?",
          "Any errors in the last hour?",
          "Any errors in the last hour?",
        ]}
      />
    </div>
  );
}
