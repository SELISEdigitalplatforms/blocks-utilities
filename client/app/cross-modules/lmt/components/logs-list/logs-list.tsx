
import { Loader } from "lucide-react";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { LogItem } from "./log-item";
import { InfiniteScroll } from "@/components/infinite-scroller";
import { useContext } from "react";
import { LogsViewerContext } from "../logs-viewer";
import { useLogs } from "../../hooks/use-logs";
import { LogsFilterToolbar } from "../logs-header/logs-filter-toolbar";
import { ILog } from "../../models/log.model";

// UI: New Data Available Indicator
const NewDataAvailableIndicator = ({ onBottomClick }: { onBottomClick: () => void }) => (
  <div
    className="absolute bottom-4 left-1/2 inline-flex h-9 -translate-x-1/2 cursor-pointer items-center justify-center whitespace-nowrap rounded-sm bg-primary px-4 py-2 text-sm font-medium text-primary-foreground ring-offset-background transition-colors hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
    onClick={(e) => {
      e.stopPropagation();
      onBottomClick();
    }}
  >
    New logs available
  </div>
);

// UI: Loading Indicator for Older Data
const OldDataFetchingIndicator = () => (
  <div className="flex w-full items-center justify-center py-4">
    <Loader size={80} className="animate-spin text-gray-400" />
  </div>
);

export const LogsList = () => {
  const { selectedService, filter, pageSize } = useContext(LogsViewerContext);
  const { level, startDate, endDate, search } = filter || {
    level: "",
    startDate: "",
    endDate: "",
    search: "",
  };

  const { serviceName } = selectedService || { serviceName: "" };

  const initialTimeStamp = endDate ? endDate : new Date().toISOString();

  const { initialLogs, isLoading, hasTopMore, fetchOldLogs, fetchNewLogs } = useLogs({
    serviceName,
    search: search,
    level,
    startDate,
    endDate: initialTimeStamp,
    pageSize,
  });

  const fetchNewLogsHandler = async (lastItemTimestamp: string = initialTimeStamp) => {
    if (search || level || startDate || endDate) return [];
    return await fetchNewLogs(lastItemTimestamp);
  };

  return (
    <Card className="relative">
      <CardHeader>
        <LogsFilterToolbar />
      </CardHeader>
      <CardContent className="h-[calc(100vh-340px)]">
        {isLoading ? (
          <div className="grid h-full w-full gap-2 overflow-hidden">
            {Array.from({ length: 20 }).map((_, index) => (
              <Skeleton key={index} className="h-12 w-full rounded-lg" />
            ))}
          </div>
        ) : (
          <InfiniteScroll<ILog>
            loadingIndicator={<OldDataFetchingIndicator />}
            initialData={initialLogs}
            hasTopMore={hasTopMore}
            topFn={(item) => {
              if (!item) return Promise.resolve([]);
              return fetchOldLogs(item.timestamp);
            }}
            pollingInterval={5000}
            pollingFn={(item) => fetchNewLogsHandler(item?.timestamp)}
            renderItem={(log, index) => (
              <div
                key={log.traceId + "-" + index}
                className="w-full cursor-default p-3 text-sm text-muted-foreground hover:bg-muted"
              >
                <LogItem log={log} />
              </div>
            )}
            bottomIndicator={(cb) => <NewDataAvailableIndicator onBottomClick={cb} />}
          />
        )}
      </CardContent>
    </Card>
  );
};
