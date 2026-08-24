import { useMutation } from "@tanstack/react-query";
import type { FindDataRequest } from "../models/subscription-simulation-harness.model";
import { subscriptionSimulationHarnessService } from "../services/subscription-simulation-harness.service";

export const useFindData = () =>
  useMutation({
    mutationFn: ({
      subscriptionId,
      logicalCollection,
      request,
    }: {
      subscriptionId: string;
      logicalCollection: string;
      request: FindDataRequest;
    }) => subscriptionSimulationHarnessService.findData(subscriptionId, logicalCollection, request),
  });
