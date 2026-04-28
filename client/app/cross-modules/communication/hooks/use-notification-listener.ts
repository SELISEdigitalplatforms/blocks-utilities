import { useEffect } from "react";

/**
 * Hook to listen for custom notification events on the window object
 * @param notificationName - The name of the custom event to listen for
 * @param callback - Function to call when the event is triggered
 */
export const useNotificationListener = (
  notificationName: string,
  callback: (data: any) => void,
) => {
  useEffect(() => {
    const handleNotification = (event: CustomEvent) => {
      callback(event.detail);
    };

    window.addEventListener(notificationName, handleNotification as EventListener);

    return () => {
      window.removeEventListener(notificationName, handleNotification as EventListener);
    };
  }, [callback, notificationName]);
};
