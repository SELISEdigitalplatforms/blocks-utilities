import NotificationConfigurationList from "@blocks-communication/notification/components/notification-configuration-list";
import { Button } from "@/components/ui-kits/button/button";
import { LogMenu } from "@blocks-lmt/components";

export default function NotificationPage() {
  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex w-full justify-between text-high-emphasis">
        <h3 className="text-2xl font-bold tracking-tight">Notifications</h3>
        <div className="flex items-center gap-4">
          <Button
            size="sm"
            variant="outline"
            onClick={() =>
              window.open(
                `${import.meta.env.BLOCKS_API_BASE_URL}/communication/v1/swagger/index.html`,
                "_blank",
              )
            }
          >
            API Docs
          </Button>
          <LogMenu link="/notification/logs" />
        </div>
      </div>
      <NotificationConfigurationList />
    </div>
  );
}
