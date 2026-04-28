import { Bell } from "lucide-react";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";

export function Notification() {
  return (
    <Popover>
      <PopoverTrigger asChild>
        <button
          type="button"
          className="text-muted-foreground transition-colors hover:text-primary"
          aria-label="Notifications"
        >
          <Bell className="h-5 w-5" />
        </button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-80">
        <div className="space-y-1">
          <div className="text-sm font-semibold text-primary">Notifications</div>
          <div className="text-sm text-muted-foreground">
            Real-time notifications are being ported into the standalone client.
          </div>
        </div>
      </PopoverContent>
    </Popover>
  );
}