import { http } from "@/lib/http-client";
import {
  IGetLiveLogsPayload,
  IGetLogsByDatePayload,
  IGetLogsPayload,
  ILog,
} from "../models/log.model";
import { IAPIResponse } from "@/models/api-response";
import { LOG_ENDPOINTS } from "../constants/endpoint.constant";

export class LogService {
  async getLogs(payload: IGetLogsPayload): Promise<IAPIResponse<ILog[]>> {
    return http.post<IAPIResponse<ILog[]>>(LOG_ENDPOINTS.GET_LOGS, payload);
  }

  async getLogsByDate(payload: IGetLogsByDatePayload): Promise<IAPIResponse<ILog[]>> {
    return http.post<IAPIResponse<ILog[]>>(LOG_ENDPOINTS.GET_LOGS_BY_DATE, payload);
  }

  async getLiveLog(paylaod: IGetLiveLogsPayload): Promise<IAPIResponse<ILog[]>> {
    const url = `${LOG_ENDPOINTS.LIVE}?Name=${paylaod.serviceName}&LastDate=${paylaod.lastDate}&ProjectKey=${paylaod.projectKey}`;
    return http.get<IAPIResponse<ILog[]>>(url);
  }
}
