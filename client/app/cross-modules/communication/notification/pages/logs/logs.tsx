import React from "react";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { LogsViewer } from "@blocks-lmt/components";

export function NotificationLogs() {
  BREADCRUMB_CUSTOM_TITLES["/notification"] = "Notification";
  BREADCRUMB_CUSTOM_TITLES["/notification/logs"] = "Logs";
  return (
    <div className="flex flex-col gap-6 p-6">
      <PageBreadcrumb breadcrumbIndex={2} />
      <LogsViewer
        services={[
          {
            id: "blocks-notification-api",
            label: "Api",
            serviceName: "blocks-notification-api",
          },
          {
            id: "blocks-notification-worker",
            label: "Worker",
            serviceName: "blocks-notification-worker",
          },
        ]}
        predefinedQueries={[
          "Has anyone faced any notification issues?",
          "Any errors in the last hour?",
          "Any errors in the last hour?",
        ]}
      />
    </div>
  );
}
