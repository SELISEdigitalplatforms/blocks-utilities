import { API_BASES } from "@/constants/endpoint.constant";

export const NOTIFICATION_ENDPOINTS = {
  GET_NOTIFICATIONS: `${API_BASES.COMMUNICATION}/Notifier/GetNotifications`,
  MARK_AS_READ: `${API_BASES.COMMUNICATION}/Notifier/MarkNotificationAsRead`,
  MARK_ALL_AS_READ: `${API_BASES.COMMUNICATION}/Notifier/MarkAllNotificationAsRead`,
} as const;

export const NOTIFICATION_CONFIG_ENDPOINTS = {
  GET_CONFIGS: `${API_BASES.CLOUD_CONFIGURATION}/Notification/Gets`,
  SAVE_CONFIG: `${API_BASES.CLOUD_CONFIGURATION}/Notification/Save`,
  DELETE_CONFIG: `${API_BASES.CLOUD_CONFIGURATION}/Notification/Delete`,
} as const;
