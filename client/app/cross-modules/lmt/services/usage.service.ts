import { http } from "@/lib/http-client";
import {
  IGetOperationalAnalyticsPayload,
  IGetServiceAnalyticsPayload,
  UsageMatrix,
} from "../models/usage.model";
import { TRACE_ENDPOINTS } from "../constants/endpoint.constant";

export class UsageService {
  getOperationalAnalytics(payload: IGetOperationalAnalyticsPayload): Promise<UsageMatrix[]> {
    return http.post(TRACE_ENDPOINTS.GET_OPERATIONAL_ANALYTICS, payload);
  }
  getServiceAnalytics(payload: IGetServiceAnalyticsPayload): Promise<UsageMatrix[]> {
    return http.post(TRACE_ENDPOINTS.GET_SERVICE_ANALYTICS, payload);
  }
}
