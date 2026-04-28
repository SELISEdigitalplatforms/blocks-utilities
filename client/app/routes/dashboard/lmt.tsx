"use client";

import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import {
	Select,
	SelectContent,
	SelectItem,
	SelectTrigger,
	SelectValue,
} from "@/components/ui-kits/select/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { cn } from "@/lib/utils";
import { useProjectStore } from "@/store/useProjectStore";
import {
	CircleAlert,
	CircleCheck,
	Clock,
	Network,
	RefreshCcw,
} from "lucide-react";
import { useQueryState, parseAsString } from "nuqs";
import { useState } from "react";
import { LMTQueryAgentSheet } from "@blocks-ai/components/lmt-query-agent/lmt-query-agent-sheet";
import { UsageServiceCard, UsageSummaryCard } from "@blocks-lmt/components";
import { USAGES_SERVICE_MAP, type UsageServiceMap } from "@blocks-lmt/constants/usage.constant";
import { useUsagesMetrics } from "@blocks-lmt/hooks/use-usage";
import {
	abbreviateDurationMs,
	abbreviateNumber,
	defaultUsagesMetrics,
} from "@blocks-lmt/utils";
import { TracesOverview } from "@blocks-lmt/components/traces-overview/traces-overview";

export default function LmtPage() {
	const tenantId = useProjectStore().selectedProject?.tenantId || "";
	const [activeTab, setActiveTab] = useQueryState("tab", parseAsString.withDefault("usage"));
	const [timeRange, setTimeRange] = useState("1h");
	const { data, isLoading, isFetching, refetch } = useUsagesMetrics({
		timeRange,
		projectKey: tenantId,
	});

	const defaultUsageData = {
		api: defaultUsagesMetrics,
		worker: defaultUsagesMetrics,
	};

	return (
		<main className="flex flex-col gap-6 p-6">
			<div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-center">
				<div>
					<h1 className="text-xl font-semibold md:text-2xl">LMT</h1>
					<p className="text-muted-foreground">
						Monitor usage, logs access, and tracing for the selected project.
					</p>
				</div>
			</div>

			<Tabs value={activeTab} onValueChange={setActiveTab}>
				<div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-center">
					<TabsList>
						<TabsTrigger value="usage">Usage</TabsTrigger>
						<TabsTrigger value="tracing">Tracing</TabsTrigger>
					</TabsList>
					{activeTab === "usage" ? (
						<div className="flex items-center gap-2">
							<Select value={timeRange} onValueChange={setTimeRange}>
								<SelectTrigger className="w-40">
									<SelectValue />
								</SelectTrigger>
								<SelectContent>
									<SelectItem value="1h">Last Hour</SelectItem>
									<SelectItem value="24h">Last 24 Hours</SelectItem>
									<SelectItem value="7d">Last 7 Days</SelectItem>
									<SelectItem value="30d">Last 30 Days</SelectItem>
								</SelectContent>
							</Select>
							<Button
								type="button"
								variant="outline"
								size="sm"
								onClick={() => refetch()}
								disabled={isLoading || isFetching || !tenantId}
							>
								<RefreshCcw
									className={cn("aspect-square w-4", (isLoading || isFetching) && "animate-spin")}
								/>
								<span className="sr-only sm:not-sr-only sm:ml-2">Refresh</span>
							</Button>
						</div>
					) : (
						<div className="flex items-center gap-2">
							<LMTQueryAgentSheet
								description="Hello! I can help you search and analyze your logs, metrics, and tracing data."
								questions={[
									"Show me traces for the last 1 hour",
									"Which services are generating the most traces",
									"Which traces had high latency today",
								]}
							/>
						</div>
					)}
				</div>

				<TabsContent value="usage" className="mt-6 space-y-6">
					<Card>
						<CardHeader>
							<CardTitle>Global overview</CardTitle>
						</CardHeader>
						<CardContent className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
							<UsageSummaryCard
								description="Total API calls"
								title={data ? abbreviateNumber(data.accumulatedApiCall) : ""}
								isLoading={isLoading || isFetching}
								Icon={Network}
							/>

							<UsageSummaryCard
								description="Average response time"
								title={data ? abbreviateDurationMs(data.accumulatedAverageDuration) : ""}
								isLoading={isLoading || isFetching}
								Icon={Clock}
								className="bg-blocks-secondary-50 text-blocks-secondary-600"
							/>

							<UsageSummaryCard
								description="Successful calls"
								title={data ? abbreviateNumber(data.accumulatedSuccess) : ""}
								isLoading={isLoading || isFetching}
								className="bg-green-50 text-green-600"
								Icon={CircleCheck}
							/>

							<UsageSummaryCard
								description="Total errors"
								title={data ? abbreviateNumber(data.accumulatedError) : ""}
								isLoading={isLoading || isFetching}
								className="bg-red-50 text-red-600"
								Icon={CircleAlert}
							/>
						</CardContent>
					</Card>

					{tenantId ? (
						<>
							<div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
								{(Object.keys(USAGES_SERVICE_MAP) as Array<keyof UsageServiceMap>).map((item) => (
									<UsageServiceCard
										key={item}
										name={USAGES_SERVICE_MAP[item].label}
										logLink={`/services/lmt/logs/${item}`}
										isLoading={isLoading || isFetching}
										metrics={data?.services[item] ?? defaultUsageData}
									/>
								))}
							</div>

							{data && (
								<div className="border-t pt-4 text-center text-xs text-medium-emphasis">
									Last updated: {new Date(data.endTime).toLocaleDateString()} at{" "}
									{new Date(data.endTime).toLocaleTimeString()}
								</div>
							)}
						</>
					) : (
						<Card>
							<CardContent className="flex h-32 items-center justify-center text-sm text-muted-foreground">
								Select a project to load LMT usage data.
							</CardContent>
						</Card>
					)}
				</TabsContent>

				<TabsContent value="tracing" className="mt-6">
					{tenantId ? (
						<TracesOverview projectKey={tenantId} />
					) : (
						<Card>
							<CardContent className="flex h-32 items-center justify-center text-sm text-muted-foreground">
								Select a project to load tracing data.
							</CardContent>
						</Card>
					)}
				</TabsContent>
			</Tabs>
		</main>
	);
}
