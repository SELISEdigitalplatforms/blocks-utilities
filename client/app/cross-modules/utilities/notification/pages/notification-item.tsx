import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { notificationClientService } from "@blocks-utilities/notification/services/notification-client.service";
import { Bell } from "lucide-react";
import { useEffect, useRef, useState, useCallback } from "react";
import {
  useGetBlocksNotificationConfig,
  useGetNotifications,
  useMarkAllAsRead,
  useMarkAsRead,
} from "@blocks-utilities/notification/hooks/use-notifications";
import { useQueryClient } from "@tanstack/react-query";
import { IDenormalizedPayload, INotification } from "@blocks-utilities/notification/models/notification.model";
import { notificationService } from "@blocks-utilities/notification/services/notification.service";

export const Notification = () => {
  const { data: configData } = useGetBlocksNotificationConfig(0, 100);
  const queryClient = useQueryClient();

  useEffect(() => {
    configData?.configurations.forEach((config) => {
      notificationClientService.connection.on(`${config.notifyMethod}`, (message: string) => {
        notificationService.getNotificationConfig(config, message);
        if (config.notifyMethod === "translate-all") {
          queryClient.invalidateQueries({ queryKey: ["get-blocksLanguageKeys"] });
        }

        try {
          let notice;
          if (typeof message === "string") {
            notice = JSON.parse(message);
          } else {
            notice = message;
          }

          let denom: IDenormalizedPayload = {
            title: "",
            description: "",
            redirectPath: "",
            toastable: false,
            meta: "",
          };
          denom = JSON.parse(notice.denormalizedPayload) as IDenormalizedPayload;
          if (
            denom &&
            denom.title &&
            denom.description &&
            (denom.title != "" || denom.description != "")
          ) {
            queryClient.invalidateQueries({ queryKey: ["notifications"] });
          }
        } catch (error) {
          console.error("Error invalidating queries:", error);
        }
      });
    });
  }, [configData, queryClient]);

  // Infinite scroll state
  const [pageNumber, setPageNumber] = useState(1);
  const [notifications, setNotifications] = useState<INotification[]>([]);
  const [hasMore, setHasMore] = useState(true);

  const { data, isLoading, isFetching } = useGetNotifications(pageNumber, 10);
  const markAsRead = useMarkAsRead();
  const markAllAsRead = useMarkAllAsRead();

  // Merge notifications on data change
  useEffect(() => {
    if (data?.notifications) {
      setNotifications((prev) => {
        // Avoid duplicates by id
        const ids = new Set(prev.map((n) => n.id));
        const merged = [...prev];
        data.notifications.forEach((n) => {
          if (!ids.has(n.id)) merged.push(n);
        });
        // Sort by createdTime descending (newest first)
        merged.sort(
          (a, b) => new Date(b.createdTime || 0).getTime() - new Date(a.createdTime || 0).getTime(),
        );
        return merged;
      });
      setHasMore(pageNumber * 10 < (data?.totalNotificationsCount || 0));
    }
  }, [data, pageNumber]);

  // Scroll handler
  const listRef = useRef<HTMLDivElement>(null);
  const handleScroll = useCallback(() => {
    const el = listRef.current;
    if (!el || isLoading || isFetching || !hasMore) return;
    if (el.scrollTop + el.clientHeight >= el.scrollHeight - 10) {
      setPageNumber((p) => p + 1);
    }
  }, [isLoading, isFetching, hasMore]);

  // Reset on popover open
  const [open, setOpen] = useState(false);
  useEffect(() => {
    if (open) {
      setPageNumber(1);
      setHasMore(true);
      queryClient.invalidateQueries({ queryKey: ["notifications"] });
    }
  }, [open, queryClient]);

  function MarkNotificationAsRead(notificationId: string) {
    return () => {
      markAsRead.mutate(notificationId, {
        onSuccess: () => {
          setNotifications((prev) => {
            const updated = prev.map((n) => {
              if (n.id === notificationId) {
                return { ...n, isRead: true };
              }
              return n;
            });
            return updated;
          });
          // Invalidate queries to refresh the unread count
          queryClient.invalidateQueries({ queryKey: ["notifications"] });
        },
      });
    };
  }

  function formatKBTitle(str: string) {
    if (!str) return "No Title";

    if (str === "agent_kb_processing_status") {
      return "AI Agent Knowledge Update Status";
    }

    return str
      .split("_")
      .map((word: string) => word.charAt(0).toUpperCase() + word.slice(1).toLowerCase())
      .join(" ");
  }

  function getKBMetaInfo(meta: string | { kb_id?: string; status?: string }): {
    kb_id?: string;
    status?: string;
  } {
    try {
      if (typeof meta === "string") {
        return JSON.parse(meta);
      }
      if (typeof meta === "object" && meta !== null) {
        return meta;
      }
    } catch (error) {
      console.error("Failed to parse meta:", error);
    }

    return {};
  }

  function formatKBMetaDescription(
    meta: string | { kb_id?: string; status?: string },
    fallbackDescription?: string,
  ): string {
    const metaInfo = getKBMetaInfo(meta);

    if (metaInfo.status && metaInfo.kb_id) {
      return `Status: ${metaInfo.status.charAt(0).toUpperCase() + metaInfo.status.slice(1)} | KB Id: ${metaInfo.kb_id.split("-")[0]}`;
    }

    if (metaInfo.status) {
      return `Status: ${metaInfo.status.charAt(0).toUpperCase() + metaInfo.status.slice(1)}`;
    }

    if (metaInfo.kb_id) {
      return `KB Id: ${metaInfo.kb_id.split("-")[0]}`;
    }

    return fallbackDescription || "No Description";
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <div className="relative" data-testid="notification-bell">
          <Bell className="h-5 w-5 cursor-pointer text-muted-foreground transition-colors hover:text-primary" />
          {data && data.unReadNotificationsCount > 0 && (
            <span
              className={`absolute -right-[6px] -top-[8px] flex items-center justify-center rounded-full bg-blue-500 font-medium text-white ${data.unReadNotificationsCount > 99 ? "h-[18px] w-[18px] text-[8px]" : "h-[16px] w-[16px] text-[10px]"}`}
            >
              {data.unReadNotificationsCount > 99 ? "99+" : data.unReadNotificationsCount}
            </span>
          )}
        </div>
      </PopoverTrigger>
      <PopoverContent className="w-[370px] p-0">
        <div className="flex items-center justify-between border-b px-4 py-3">
          <div className="text-base font-semibold text-primary">Notifications</div>
          <button
            onClick={() => {
              markAllAsRead.mutate(undefined, {
                onSuccess: () => {
                  setNotifications((prev) => prev.map((n) => ({ ...n, isRead: true })));
                  queryClient.invalidateQueries({ queryKey: ["notifications"] });
                },
              });
            }}
            className="text-xs text-muted-foreground hover:text-primary hover:underline"
          >
            Mark all as read
          </button>
        </div>

        <div className="max-h-[400px] overflow-y-auto" ref={listRef} onScroll={handleScroll}>
          {isLoading && notifications.length === 0 ? (
            <div className="p-4 text-center text-sm text-muted-foreground"></div>
          ) : notifications.length === 0 ? (
            <div className="p-4 text-center text-sm text-muted-foreground">No notifications</div>
          ) : (
            notifications.map((notification, idx) => (
              <div
                key={notification.id || idx}
                className={
                  "flex cursor-pointer items-start gap-3 border-b px-4 py-3 last:border-b-0" +
                  (!notification.isRead ? " bg-muted/60" : "")
                }
                onMouseEnter={() => {
                  if (!notification.isRead) {
                    MarkNotificationAsRead(notification.id)();
                  }
                }}
              >
                <div className="min-w-0 flex-1">
                  {(() => {
                    let denorm: IDenormalizedPayload = {
                      title: "",
                      description: "",
                      redirectPath: "",
                      toastable: false,
                      meta: "",
                    };
                    try {
                      denorm = JSON.parse(notification.denormalizedPayload) as IDenormalizedPayload;
                    } catch {
                      console.error(
                        "Failed to parse denormalized payload",
                        notification.denormalizedPayload,
                      );
                    }
                    return (
                      <>
                        <div className="text-sm font-medium">
                          {formatKBTitle(denorm.title) || "No Title"}
                        </div>
                        <div className="text-xs text-muted-foreground">
                          {formatKBMetaDescription(denorm.meta, denorm.description)}
                        </div>
                        <div className="mt-1 text-[11px] text-muted-foreground">
                          {notification.createdTime
                            ? new Date(notification.createdTime).toLocaleString()
                            : ""}
                        </div>
                      </>
                    );
                  })()}
                </div>
              </div>
            ))
          )}
          {isFetching && notifications.length > 0 && (
            <div className="p-2 text-center text-xs text-muted-foreground">Loading ...</div>
          )}
        </div>
      </PopoverContent>
    </Popover>
  );
};
