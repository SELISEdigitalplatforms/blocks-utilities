import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { LogsViewer } from "@blocks-lmt/components";

export function IamLogs() {
  BREADCRUMB_CUSTOM_TITLES["/services/iam"] = "IAM";
  BREADCRUMB_CUSTOM_TITLES["/services/iam/logs"] = "Logs";

  return (
    <div>
      <PageBreadcrumb breadcrumbIndex={2} />
      <LogsViewer
        services={[
          {
            id: "blocks-idp-api",
            label: "API",
            serviceName: "blocks-idp-api",
          },
          {
            id: "blocks-idp-worker",
            label: "Worker",
            serviceName: "blocks-idp-worker",
          },
        ]}
        predefinedQueries={[
          "Show recent IAM user-management errors.",
          "Were there unusual permission or role update failures today?",
          "Any spikes in user provisioning issues over the last 24 hours?",
        ]}
      />
    </div>
  );
}
