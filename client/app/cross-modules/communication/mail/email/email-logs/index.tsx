

import React from "react";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { LogsViewer } from "@blocks-lmt/components";

export function EmailLogs() {
  BREADCRUMB_CUSTOM_TITLES["/utilities/email"] = "Email";
  BREADCRUMB_CUSTOM_TITLES["/utilities/email/logs"] = "Logs";
  return (
    <div>
      <PageBreadcrumb breadcrumbIndex={2} />
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
