import { useQuery } from "@tanstack/react-query";
import { lmtService } from "../services/lmt.service";
import {
  IGetOperationalAnalyticsPayload,
  IGetServiceAnalyticsPayload,
} from "../models/usage.model";
import { getNormalizeUsageMetricsData } from "../utils/usage.util";

export const useGetOperationalAnalytics = (option: IGetOperationalAnalyticsPayload) => {
  return useQuery({
    queryKey: ["usage-operation", option],
    queryFn: () => lmtService.usage.getOperationalAnalytics(option),
  });
};

export const useGetServiceAnalytics = (option: IGetServiceAnalyticsPayload) => {
  return useQuery({
    queryKey: ["usage-service", option],
    queryFn: () => lmtService.usage.getServiceAnalytics(option),
  });
};

export const useUsagesMetrics = (option: { timeRange: string; projectKey: string }) => {
  return useQuery({
    queryKey: ["usage-metrics", option],
    queryFn: async () => {
      const now = new Date();
      let startTime: Date;

      switch (option.timeRange) {
        case "1h":
          startTime = new Date(now.getTime() - 1 * 60 * 60 * 1000);
          break;
        case "24h":
          startTime = new Date(now.getTime() - 24 * 60 * 60 * 1000);
          break;
        case "7d":
          startTime = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);
          break;
        case "30d":
          startTime = new Date(now.getTime() - 30 * 24 * 60 * 60 * 1000);
          break;
        default:
          startTime = new Date(now.getTime() - 24 * 60 * 60 * 1000);
      }

      const payload = {
        projectKey: option.projectKey,
        startTime: startTime.toISOString(),
        endTime: now.toISOString(),
      };

      const res = await lmtService.usage.getServiceAnalytics(payload);
      return getNormalizeUsageMetricsData(res, payload);
    },
  });
};
