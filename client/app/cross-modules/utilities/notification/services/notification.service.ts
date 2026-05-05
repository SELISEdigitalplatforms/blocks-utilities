import { http } from "@/lib/http-client";
import {
  INotification,
  INotificationConfig,
} from "@blocks-utilities/notification/models/notification.model";
import {
  NOTIFICATION_CONFIG_ENDPOINTS,
  NOTIFICATION_ENDPOINTS,
} from "@blocks-utilities/notification/constants/endpoint.constant";

export class NotificationService {
  getNotifications = (
    pageNumber: number,
    pageSize: number,
  ): Promise<{
    unReadNotificationsCount: number;
    totalNotificationsCount: number;
    notifications: INotification[];
  }> => {
    const url = `${NOTIFICATION_ENDPOINTS.GET_NOTIFICATIONS}?page=${pageNumber - 1}&pageSize=${pageSize}`;
    return http.get(url);
  };

  markAsRead = (
    notificationId: string,
  ): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> => {
    return http.post(NOTIFICATION_ENDPOINTS.MARK_AS_READ, {
      id: notificationId,
    });
  };

  markAllNotificationsAsRead = (): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> => {
    return http.post(NOTIFICATION_ENDPOINTS.MARK_ALL_AS_READ, {});
  };

  getNotificationConfig = (
    config: INotificationConfig,
    message: string,
  ): void => {
    const notificationEvent = new CustomEvent(config.notifyMethod, {
      detail: {
        method: config.notifyMethod,
        message: message,
        timestamp: new Date().toISOString(),
        config: config,
      },
    });
    window.dispatchEvent(notificationEvent);
  };

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
