import { http } from "@/lib/http-client";
import { INotificationConfig } from "../models/notification.model";
import { NOTIFICATION_CONFIG_ENDPOINTS } from "../constants/endpoint.constant";

export class NotificationService {
  getNotificationConfigs = (
    page: number = 0,
    pageSize: number = 10,
    projectKey: string,
  ): Promise<{
    configurations: INotificationConfig[];
    totalCount: number;
    errors: null | unknown;
    isSuccess: boolean;
  }> => {
    const url = `${NOTIFICATION_CONFIG_ENDPOINTS.GET_CONFIGS}?page=${page}&pageSize=${pageSize}&projectKey=${projectKey}`;
    return http.get(url);
  };

  saveNotificationConfig = (payload: {
    name: string;
    channelToNotify: number;
    notificationType: number;
    enablePersistence: boolean;
    notifyMethod: string;
    projectKey: string;
    isUpdateRequest: boolean;
    itemId?: string;
  }): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> => {
    return http.post(NOTIFICATION_CONFIG_ENDPOINTS.SAVE_CONFIG, payload);
  };

  deleteNotificationConfig = (payload: {
    itemId: string;
    projectKey: string;
  }): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> => {
    const url = `${NOTIFICATION_CONFIG_ENDPOINTS.DELETE_CONFIG}?itemId=${payload.itemId}&projectKey=${payload.projectKey}`;
    return http.delete(url);
  };
}

export const notificationService = new NotificationService();
