export interface INotification {
  id: string;
  correlationId: string;
  payload: {
    UserId: string;
    subscriptionFilters: NotificationSubScriptionFilter[];
    notificationType: string;
    responseKey: string;
    responseValue: string;
  };
  denormalizedPayload: string;
  createdTime: string;
  isRead: boolean;
  ReadByRoles: string[] | null;
}

export interface IDenormalizedPayload {
  title: string;
  description: string;
  redirectPath: string;
  toastable: boolean;
  meta:
    | {
        kb_id?: string;
        status?: string;
      }
    | string;
}

export interface INotificationConfig {
  itemId: string;
  name: string;
  channelToNotify: number;
  notificationType: number;
  enablePersistence: boolean;
  notifyMethod: string;
}

export interface NotificationSubScriptionFilter {
  context: string;
  actionName: string;
  value: string;
}
