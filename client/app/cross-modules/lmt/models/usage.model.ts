export interface UsageMatrix {
  _id: string;
  TotalRequests: number;
  Status1xx: number;
  Status2xx: number;
  Status3xx: number;
  Status4xx: number;
  Status5xx: number;
  TotalDuration: number;
  AverageDuration: number;
  PeakDuration: number;
  AverageThroughput: number | null;
  TotalThroughput: number | null;
}

export type UsageMatrixSummary = UsageMatrix & {
  totalSuccess: number;
  totalError: number;
  errorRate: number;
  successRate: number;
  callsPerMinute: number;
};

export interface IGetOperationalAnalyticsPayload {
  startTime: string;
  endTime: string;
  serviceName: string;
  projectKey: string;
  operationName?: string;
}

export interface IGetServiceAnalyticsPayload {
  startTime: string;
  endTime: string;
  serviceName?: string;
  projectKey: string;
}
