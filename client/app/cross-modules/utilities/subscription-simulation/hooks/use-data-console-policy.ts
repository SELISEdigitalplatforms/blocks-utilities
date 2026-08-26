import { useQuery } from "@tanstack/react-query";
import { subscriptionSimulationHarnessService } from "../services/subscription-simulation-harness.service";

/** Fetched only while the data console dialog is open — there's no reason to hold it otherwise. */
export const useDataConsolePolicy = (enabled: boolean) =>
  useQuery({
    queryKey: ["subscription-simulation-data-console-policy"],
    queryFn: () => subscriptionSimulationHarnessService.getDataPolicy(),
    enabled,
    staleTime: 60_000,
  });
