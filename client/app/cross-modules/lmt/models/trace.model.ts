export interface IIssue {
  icon: string;
  description: string;
}

export const getTypeColor = (type: string) => {
  switch (type) {
    case "GET":
      return "text-success";
    case "POST":
      return "text-icon-warning";
    default:
      return "text-error";
  }
};

export interface ITags {
  ecosystem: string;
  habitat: string;
  uriClient: string;
}

export interface IAttributes {
  [key: string]: string | number | undefined;
}

export interface ISecurityContext {
  TenantId: string;
  Roles: [];
  UserId: string;
  Audiances: string | null;
  RequestUri: string;
  OrganizationId: string;
  IsAuthenticated: boolean;
}

export interface IRequest {
  url: string;
  Headers: {
    host: string;
    traceparent: string;
    xBlocksKey: string;
  };
}

export interface IResponse {
  statusCode: number;
  headers: {
    contentType: string;
    date: Date;
    server: string;
    transferEncoding: string;
  };
}

export interface SubEntry {
  service?: string;
  method: string;
  url: string;
  time: string;
  subEntries?: SubEntry[];
}

export interface LogEntry {
  service: string;
  method: string;
  url: string;
  time: string;
  subEntries?: SubEntry[];
}

export const entryTypes = [
  {
    label: "GET",
    value: "GET",
  },
  {
    label: "POST",
    value: "POST",
  },
  {
    label: "DELETE",
    value: "DELETE",
  },
];

export const durations = [
  {
    value: "800ms",
    label: "800ms",
  },
  {
    value: "1620ms",
    label: "1620ms",
  },
  {
    value: "1523ms",
    label: "1523ms",
  },
  {
    value: "1200ms",
    label: "1200ms",
  },
];

export const issues = [
  {
    label: "1",
    value: "1",
  },
  {
    label: "2",
    value: "2",
  },
  {
    label: "3",
    value: "3",
  },
];

export interface ISpan {
  id: number;
  spanID: string;
  serviceName: string;
  spanName: string;
  startTime: Date;
  duration: string;
}

export const spanTableData: ISpan[] = [
  {
    id: 1,
    spanID: "87f1e5f0bada89c2",
    serviceName: "email",
    spanName: "GET /",
    startTime: new Date("19/02/2024 10:42:52.049"),
    duration: "100ms",
  },
  {
    id: 2,
    spanID: "5c67ab09f3c2f76a",
    serviceName: "email",
    spanName: "GET / api",
    startTime: new Date("19/02/2024 10:42:52.049"),
    duration: "100ms",
  },
  {
    id: 3,
    spanID: "d3a72b4e9d8c01ef",
    serviceName: "email",
    spanName: "GET /",
    startTime: new Date("19/02/2024 10:42:52.049"),
    duration: "100ms",
  },
  {
    id: 4,
    spanID: "ec4716cf292b2b9d",
    serviceName: "email",
    spanName: "GET /",
    startTime: new Date("19/02/2024 10:42:52.049"),
    duration: "100ms",
  },
  {
    id: 5,
    spanID: "b8d7c0c114e5a0b6",
    serviceName: "email",
    spanName: "GET /",
    startTime: new Date("19/02/2024 10:42:52.049"),
    duration: "100ms",
  },
  {
    id: 6,
    spanID: "2e439f14c0e7d091",
    serviceName: "email",
    spanName: "GET /",
    startTime: new Date("19/02/2024 10:42:52.049"),
    duration: "100ms",
  },
];

export interface TraceTree extends Trace {
  entryPoint: {
    method: string;
    actionName: string;
  };
  issues: IIssue[];
  tags: ITags;
  attributes: IAttributes;
  securityContext: ISecurityContext;
  request: IRequest;
  response: IResponse;
  logs: LogEntry[];
  subEntries: TraceTree[] | [];
  calculatedDuration?: number;
  calculatedStartTime?: string;
  calculatedEndTime?: string;
}

export interface Trace {
  timestamp: string;
  traceId: string;
  spanId: string;
  parentSpanId: string;
  parentId: string;
  kind: string;
  activitySourceName: string;
  operationName: string;
  startTime: string;
  endTime: string;
  duration: number;
  attributes: IAttributes;
  status: string;
  statusDescription: string;
  baggage: {
    TenantId: string;
    IsFromCloud: string;
  };
  serviceName: string;
}

export interface IGetTracesPayload {
  page: number;
  pageSize: number;
  sort?: {
    property: string;
    isDescending: boolean;
  };
  filter?: {
    startDate?: string;
    endDate?: string;
    services: string[];
    excepts: string[];
  };
  search: string;
  projectKey: string;
}
export interface IGetTracesResponse {
  totalCount: number;
  data: TraceTree[];
  errors: unknown;
}
export interface IGetTraceByTraceIdPayload {
  traceId: string;
  projectKey: string;
}
