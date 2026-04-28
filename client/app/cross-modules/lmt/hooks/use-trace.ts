import { useQuery } from "@tanstack/react-query";
import { lmtService } from "../services/lmt.service";
import { IGetTraceByTraceIdPayload, IGetTracesPayload } from "../models/trace.model";

export const useGetTraces = (option: IGetTracesPayload) => {
  return useQuery({
    queryKey: ["traces", option],
    queryFn: () => lmtService.trace.getTraces(option),
  });
};

export const useGetTraceById = (option: IGetTraceByTraceIdPayload) => {
  return useQuery({
    queryKey: ["trace", option],
    queryFn: () => lmtService.trace.getTraceByTraceId(option),
  });
};
