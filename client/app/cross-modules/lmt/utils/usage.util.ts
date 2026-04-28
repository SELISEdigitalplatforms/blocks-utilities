import { USAGES_SERVICE_MAP, type UsageServiceMap } from "@blocks-lmt/constants/usage.constant";
import type {
  IGetServiceAnalyticsPayload,
  UsageMatrix,
  UsageMatrixSummary,
} from "../models/usage.model";

export const defaultUsagesMetrics: UsageMatrixSummary = {
  _id: "",
  TotalRequests: 0,
  Status1xx: 0,
  Status2xx: 0,
  Status3xx: 0,
  Status4xx: 0,
  Status5xx: 0,
  TotalDuration: 0,
  AverageDuration: 0,
  PeakDuration: 0,
  AverageThroughput: 0,
  TotalThroughput: 0,
  totalError: 0,
  totalSuccess: 0,
  errorRate: 0,
  successRate: 0,
  callsPerMinute: 0,
};

const getTotalMinutes = (start: string, end: string): number => {
  const startDate = new Date(start).getTime();
  const endDate = new Date(end).getTime();
  const diffMs = endDate - startDate;
  const diffMinutes = diffMs / (1000 * 60);
  return diffMinutes;
};

export function abbreviateNumber(value: number): string {
  if (value < 1000) return value.toString();

  const suffixes = ["", "k", "M", "B", "T"];
  const index = Math.floor(Math.log10(value) / 3);

  const shortValue = value / Math.pow(1000, index);
  return `${shortValue.toFixed(1).replace(/\.0$/, "")}${suffixes[index]}`;
}

export function abbreviateDurationMs(ms: number): string {
  const seconds = ms / 1000;

  if (seconds < 60) {
    return `${seconds.toFixed(2)}s`;
  }

  const minutes = Math.floor(seconds / 60);
  const remainingSeconds = seconds % 60;

  if (minutes < 60) {
    return `${minutes}m ${remainingSeconds.toFixed(2)}s`;
  }

  const hours = Math.floor(minutes / 60);
  const remainingMinutes = minutes % 60;

  return `${hours}h ${remainingMinutes}m ${remainingSeconds.toFixed(2)}s`;
}

export function abbreviateBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes}B`;

  const suffixes = ["B", "K", "M", "G", "T", "P"];
  const index = Math.floor(Math.log(bytes) / Math.log(1024));

  const shortValue = bytes / Math.pow(1024, index);
  return `${shortValue.toFixed(1).replace(/\.0$/, "")}${suffixes[index]}`;
}

export function transformMatrixData(item: Partial<UsageMatrix>): UsageMatrix {
  const toNumber = (val: unknown): number => {
    const num = Number(val);
    return isNaN(num) ? 0 : num;
  };

  return {
    _id: String(item._id ?? ""), // always keep _id as string
    TotalRequests: toNumber(item.TotalRequests),
    Status1xx: toNumber(item.Status1xx),
    Status2xx: toNumber(item.Status2xx),
    Status3xx: toNumber(item.Status3xx),
    Status4xx: toNumber(item.Status4xx),
    Status5xx: toNumber(item.Status5xx),
    TotalDuration: toNumber(item.TotalDuration),
    AverageDuration: toNumber(item.AverageDuration),
    PeakDuration: toNumber(item.PeakDuration),
    AverageThroughput: toNumber(item.AverageThroughput),
    TotalThroughput: toNumber(item.TotalThroughput),
  };
}

export function getNormalizeUsageMetricsData(
  data: UsageMatrix[],
  payload: IGetServiceAnalyticsPayload,
): {
  services: Partial<
    Record<keyof UsageServiceMap, { api: UsageMatrixSummary; worker: UsageMatrixSummary }>
  >;
  accumulatedApiCall: number;
  accumulatedDuration: number;
  accumulatedSuccess: number;
  accumulatedError: number;
  endTime: string;
  accumulatedAverageDuration: number;
} {
  const modifiedData: Record<string, UsageMatrix> =
    data?.reduce((acc: Record<string, UsageMatrix>, item) => {
      acc[item._id] = item;
      return acc;
    }, {}) || {};

  const services: Partial<
    Record<keyof UsageServiceMap, { api: UsageMatrixSummary; worker: UsageMatrixSummary }>
  > = {};

  let accumulatedApiCall = 0;
  let accumulatedDuration = 0;
  let accumulatedSuccess = 0;
  let accumulatedError = 0;

  (Object.keys(USAGES_SERVICE_MAP) as Array<keyof UsageServiceMap>).forEach((key) => {
    const { apiName, workerName } = USAGES_SERVICE_MAP[key];

    const apiData = modifiedData[apiName] || defaultUsagesMetrics;
    const workerData = workerName
      ? modifiedData[workerName] || defaultUsagesMetrics
      : defaultUsagesMetrics;

    const formatSummary = (item: UsageMatrix): UsageMatrixSummary => {
      const transformedData = transformMatrixData(item);
      const totalSuccess =
        transformedData.Status1xx + transformedData.Status2xx + transformedData.Status3xx;
      const totalError = transformedData.Status4xx + transformedData.Status5xx;
      const totalRequests = Number(transformedData.TotalRequests) ?? 0;
      return {
        ...transformedData,
        totalSuccess,
        totalError,
        successRate: totalSuccess ? Number(((totalSuccess / totalRequests) * 100).toFixed(2)) : 0,
        errorRate: totalError ? Number(((totalError / totalRequests) * 100).toFixed(2)) : 0,
        callsPerMinute: Number(
          (totalRequests / Math.ceil(getTotalMinutes(payload.startTime, payload.endTime))).toFixed(
            2,
          ),
        ),
      };
    };

    const serviceEntry = {
      api: formatSummary(apiData),
      worker: {
        ...formatSummary(workerData),
        totalSuccess: 0,
        totalError: 0,
      },
    };
    services[key] = serviceEntry;
    accumulatedApiCall += serviceEntry.api.TotalRequests;
    accumulatedDuration += serviceEntry.api.TotalDuration;
    accumulatedSuccess += serviceEntry.api.totalSuccess;
    accumulatedError += serviceEntry.api.totalError;
  });

  return {
    services,
    accumulatedApiCall,
    accumulatedDuration,
    accumulatedError,
    accumulatedSuccess,
    endTime: payload.endTime,
    accumulatedAverageDuration: accumulatedApiCall
      ? Number((accumulatedDuration / accumulatedApiCall).toFixed(2))
      : 0,
  };
}
