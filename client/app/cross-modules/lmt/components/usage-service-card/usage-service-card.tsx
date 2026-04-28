
import React, { useState } from "react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { abbreviateBytes, abbreviateDurationMs, abbreviateNumber } from "../../utils/usage.util";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { UsageMatrixSummary } from "../../models/usage.model";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
import { cn } from "@/lib/utils";
import { Activity, Cpu, Info, Logs } from "lucide-react";
import { Link } from "react-router-dom";

interface ServiceCardProps {
  isLoading: boolean;
  name: string;
  logLink?: string;
  metrics: {
    api: UsageMatrixSummary;
    worker: UsageMatrixSummary;
  };
}

const UsageServiceCardSkelton = ({ name }: { name: string }) => (
  <Card className="border shadow-none transition-shadow duration-200 hover:shadow-md">
    <CardHeader className="flex flex-row items-center justify-between">
      <CardTitle className="text-lg font-semibold text-high-emphasis">{name}</CardTitle>
    </CardHeader>

    <CardContent>
      <Skeleton className="h-16 w-full" />
      <Skeleton className="mt-2 h-16 w-full" />
      <div className="mt-4 grid gap-1.5">
        <div className="flex items-center justify-between">
          <span className="text-sm text-medium-emphasis">Calls/min</span>
          <Skeleton className="h-4 w-1/2" />
        </div>
        <div className="flex items-center justify-between">
          <span className="text-sm text-medium-emphasis">Peak Response</span>
          <Skeleton className="h-4 w-1/2" />
        </div>
        <div className="flex items-center justify-between">
          <span className="text-sm text-medium-emphasis">Throughput</span>
          <Skeleton className="h-4 w-1/2" />
        </div>
      </div>
    </CardContent>
  </Card>
);

export const UsageServiceCard: React.FC<ServiceCardProps> = ({
  name,
  logLink,
  metrics,
  isLoading,
}) => {
  const [selected, setSelected] = useState<"api" | "worker">("api");

  if (isLoading) return <UsageServiceCardSkelton name={name} />;

  const currentMatrix = metrics[selected];
  const mobileItemClassName =
    "flex h-7 w-7 items-center justify-center rounded-lg transition-all duration-200 md:h-8 md:w-auto md:px-2 md:gap-1 md:rounded-[8px]";

  return (
    <Card className="border shadow-none transition-shadow duration-200 hover:shadow-md">
      <CardHeader className="gap-2">
        <div className="mb-2 flex flex-col gap-2 md:flex-row md:items-center md:justify-between md:gap-3">
          <div className="space-y-1">
            <CardTitle className="text-lg font-semibold text-high-emphasis">{name}</CardTitle>
            <p className="text-xs text-medium-emphasis sm:text-sm">Inspect request path, background work, and logs.</p>
          </div>

          <div className="flex items-center gap-1 rounded-lg border border-border/70 bg-surface-app/80 p-1 md:gap-0.5">
            <button
              type="button"
              onClick={() => setSelected("api")}
              className={cn(
                mobileItemClassName,
                selected === "api"
                  ? "bg-background text-high-emphasis shadow-sm"
                  : "text-medium-emphasis hover:bg-background/70 hover:text-high-emphasis",
              )}
              title="API metrics"
            >
              <Activity className="h-3 w-3" />
              <span className={cn("hidden md:inline text-xs font-medium")}>API</span>
            </button>

            <button
              type="button"
              onClick={() => setSelected("worker")}
              className={cn(
                mobileItemClassName,
                selected === "worker"
                  ? "bg-background text-high-emphasis shadow-sm"
                  : "text-medium-emphasis hover:bg-background/70 hover:text-high-emphasis",
              )}
              title="Worker metrics"
            >
              <Cpu className="h-3 w-3" />
              <span className={cn("hidden md:inline text-xs font-medium")}>Worker</span>
            </button>

            {logLink ? (
              <Link
                to={logLink}
                className={cn(
                  mobileItemClassName,
                  "border border-transparent text-medium-emphasis hover:border-border/80 hover:bg-background hover:text-high-emphasis",
                )}
                title="View logs"
              >
                <Logs className="h-3 w-3" />
                <span className={cn("hidden md:inline text-xs font-medium")}>Logs</span>
              </Link>
            ) : (
              <div
                className={cn(
                  mobileItemClassName,
                  "cursor-not-allowed border border-dashed border-border/70 text-disabled opacity-70",
                )}
                title="Logs unavailable"
              >
                <Logs className="h-3 w-3" />
                <span className={cn("hidden md:inline text-xs font-medium")}>Logs</span>
              </div>
            )}
          </div>
        </div>
      </CardHeader>

      <CardContent>
        <div className="flex h-16 flex-col justify-center rounded-sm bg-surface-app px-3 py-2">
          <div className="flex items-center justify-between text-high-emphasis">
            <h3 className="text-lg font-normal">API calls</h3>
            <h3 className="text-xl font-semibold">
              {abbreviateNumber(currentMatrix.TotalRequests)}
            </h3>
          </div>
          {selected === "api" && (
            <div className="mt-1 flex items-center gap-2 text-sm font-medium text-medium-emphasis">
              <div>
                Success:{" "}
                <span className="text-green-700">
                  {abbreviateNumber(currentMatrix.totalSuccess)} ({currentMatrix.successRate}%)
                </span>
              </div>
              <div className="aspect-square w-1 rounded-full bg-blocks-primary-50"></div>
              <div>
                Error:{" "}
                <span className="text-red-700">
                  {abbreviateNumber(currentMatrix.totalError)} ({currentMatrix.errorRate}%)
                </span>
              </div>
              <div className="aspect-square w-1 rounded-full bg-blocks-primary-50"></div>
              <TooltipProvider>
                <Tooltip>
                  <TooltipTrigger>
                    <Info className="aspect-square w-4" />
                  </TooltipTrigger>
                  <TooltipContent>
                    <div className="grid grid-cols-1 gap-4 md:grid-cols-2 md:gap-10">
                      <div className="flex flex-col gap-1">
                        <h4>Success Series</h4>
                        <div className="flex items-center justify-between">
                          <span>1xx</span>
                          <span>{abbreviateNumber(currentMatrix.Status1xx)}</span>
                        </div>
                        <div className="flex items-center justify-between">
                          <span>2xx</span>
                          <span>{abbreviateNumber(currentMatrix.Status2xx)}</span>
                        </div>
                        <div className="flex items-center justify-between">
                          <span>3xx</span>
                          <span>{abbreviateNumber(currentMatrix.Status3xx)}</span>
                        </div>
                      </div>
                      <div className="flex flex-col gap-1">
                        <h4>Error Series</h4>
                        <div className="flex items-center justify-between">
                          <span>4xx</span>
                          <span>{abbreviateNumber(currentMatrix.Status4xx)}</span>
                        </div>
                        <div className="flex items-center justify-between">
                          <span>5xx</span>
                          <span>{abbreviateNumber(currentMatrix.Status5xx)}</span>
                        </div>
                        <div></div>
                      </div>
                    </div>
                  </TooltipContent>
                </Tooltip>
              </TooltipProvider>
            </div>
          )}
        </div>

        <div className="mt-3 flex h-16 items-center justify-between rounded-sm bg-surface-app px-3 py-2">
          <h3 className="text-lg font-normal">Average duration</h3>
          <h3 className="text-xl font-semibold">
            {abbreviateDurationMs(currentMatrix.AverageDuration)}
          </h3>
        </div>

        <div className="mt-4 grid gap-1.5">
          <div className="flex items-center justify-between">
            <span className="text-sm text-medium-emphasis">Calls/min</span>
            <span className="text-sm font-semibold text-high-emphasis">
              {currentMatrix.callsPerMinute}
            </span>
          </div>
          <div className="flex items-center justify-between">
            <span className="text-sm text-medium-emphasis">Peak Response</span>
            <span className="text-sm font-semibold text-high-emphasis">
              {abbreviateDurationMs(currentMatrix.PeakDuration)}
            </span>
          </div>
          <div className="flex items-center justify-between">
            <span className="text-sm text-medium-emphasis">Throughput</span>
            <span className="text-sm font-semibold text-high-emphasis">
              {abbreviateBytes(currentMatrix.TotalThroughput || 0)}
            </span>
          </div>
        </div>
      </CardContent>
    </Card>
  );
};
