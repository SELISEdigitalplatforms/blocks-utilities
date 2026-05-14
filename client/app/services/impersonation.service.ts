import { deriveIdpBaseUrl } from "@/lib/blocks-url.util";
import { HttpClient } from "@/lib/http-client";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { debug } from "@/lib/debug";


const idpHttp = new HttpClient(
  deriveIdpBaseUrl(),
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

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
    debug.group("[ImpersonationService] startImpersonation");
    debug.log("Request:", request);
    debug.log("IDP base:", deriveIdpBaseUrl());
    debug.log("X-Blocks-Key:", getRuntimeEnv("BLOCKS_X_BLOCKS_KEY"));
    debug.groupEnd();
    return idpHttp.post(`${IMPERSONATION_BASE}/impersonate`, request);
  }

  stopImpersonation(): Promise<void> {
    debug.group("[ImpersonationService] stopImpersonation");
    debug.groupEnd();
    return idpHttp.post(`${IMPERSONATION_BASE}/impersonation/stop`, null);
  }
}

export const impersonationService = new ImpersonationService();
