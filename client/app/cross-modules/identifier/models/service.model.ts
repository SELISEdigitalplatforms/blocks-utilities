// API Request/Response types based on actual endpoints
export interface ServiceRegistrationRequest {
  serviceName: string;
  serviceUrl: string;
  environment: string;
  serviceType: string;
  description: string;
  metadata: string;
  projectId: string;
}

export interface ServiceRegistrationResponse {
  id?: string;
  serviceName?: string;
  serviceUrl?: string;
  environment?: string;
  serviceType?: string;
  description?: string;
  metadata?: string;
  projectId?: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface ServiceListResponse {
  data?: RegisteredService[];
  total?: number;
  success?: boolean;
  message?: string;
}

// Internal UI types
export type ServiceType = "api" | "worker" | "web" | "mobile" | "database" | "microservice";

export type Environment = "production" | "staging" | "development" | "testing";

export interface ServiceMetadata {
  [key: string]: string | number | boolean;
}

export interface RegisteredService {
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  language: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  name: string;
  url: string;
  environment: string;
  type: number;
  description: string;
  serviceId: string;
  metadata: Record<string, string>;
  serviceBusConnectionString: string;
  tenantId: string;
  serviceType: string;
}

export interface LogEntry {
  id: string;
  service_id: string;
  timestamp: string;
  level: "debug" | "info" | "warn" | "error" | "fatal";
  message: string;
  source?: string;
  metadata?: Record<string, unknown>;
}

export interface TraceEntry {
  id: string;
  service_id: string;
  trace_id: string;
  span_id: string;
  parent_span_id?: string;
  operation_name: string;
  start_time: string;
  end_time: string;
  duration_ms: number;
  status: "ok" | "error" | "timeout";
  tags?: Record<string, string>;
  logs?: Array<{
    timestamp: string;
    fields: Record<string, unknown>;
  }>;
}

export interface TraceEntry {
  id: string;
  service_id: string;
  trace_id: string;
  span_id: string;
  parent_span_id?: string;
  operation_name: string;
  start_time: string;
  end_time: string;
  duration_ms: number;
  status: "ok" | "error" | "timeout";
  tags?: Record<string, string>;
  logs?: Array<{
    timestamp: string;
    fields: Record<string, unknown>;
  }>;
}

export interface ServicesSummary {
  services: RegisteredService[];
  total_count: number;
  active_count: number;
  error_count: number;
}
