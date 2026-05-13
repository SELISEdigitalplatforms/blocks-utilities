import { http } from "@/lib/http-client";

const IMPERSONATION_BASE = "/api/Authentication";

export interface ImpersonationRequest {
  targetTenantId: string;
  orgId?: string;
  organizationId?: string;
}

export interface ImpersonationState {
  rootTenantId: string;
  targetTenantId: string;
  orgId: string;
  startedAtUtc: string;
}

class ImpersonationService {
  startImpersonation(request: ImpersonationRequest): Promise<ImpersonationState> {
    return http.post(`${IMPERSONATION_BASE}/impersonate`, request);
  }

  stopImpersonation(): Promise<void> {
    return http.post(`${IMPERSONATION_BASE}/impersonation/stop`, null);
  }
}

export const impersonationService = new ImpersonationService();
