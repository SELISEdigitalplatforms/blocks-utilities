import { deriveIdpBaseUrl } from "@/lib/blocks-url.util";
import { HttpClient } from "@/lib/http-client";
import { getRuntimeEnv } from "@/lib/runtime-env";

const IMPERSONATION_BASE = "/api/Authentication";

const idpHttp = new HttpClient(
  deriveIdpBaseUrl(),
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

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
    return idpHttp.post(`${IMPERSONATION_BASE}/impersonate`, request);
  }

  stopImpersonation(): Promise<void> {
    return idpHttp.post(`${IMPERSONATION_BASE}/impersonation/stop`, null);
  }
}

export const impersonationService = new ImpersonationService();
