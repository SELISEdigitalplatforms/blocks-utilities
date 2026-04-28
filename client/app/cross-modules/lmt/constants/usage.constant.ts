import { SERVICES, type ServiceApiName, type ServiceWorkerName } from "./services.constant";

const usageServices = SERVICES.filter((s) => s.showInUsage);

type UsageServiceMapKey = (typeof usageServices)[number]["name"];
type UsageServiceMapValue = {
  label: string;
  apiName: ServiceApiName;
  workerName: ServiceWorkerName;
};

export type UsageServiceMap = Record<UsageServiceMapKey, UsageServiceMapValue>;

export const USAGES_SERVICE_MAP: UsageServiceMap = usageServices.reduce(
  (acc, { name, label, serviceName }) => {
    acc[name] = {
      label,
      apiName: `blocks-${serviceName}-api`,
      workerName: `blocks-${serviceName}-worker`,
    };
    return acc;
  },
  {} as UsageServiceMap,
);
