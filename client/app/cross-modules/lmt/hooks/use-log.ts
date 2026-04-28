import { useQuery } from "@tanstack/react-query";
import { IGetLiveLogsPayload, IGetLogsPayload } from "../models/log.model";
import { lmtService } from "../services/lmt.service";

export const useGetLogs = (option: IGetLogsPayload) => {
  return useQuery({
    queryKey: ["logs", option],
    queryFn: () => lmtService.log.getLogs({ ...option }),
  });
};

export const useGetLiveLogs = (option: IGetLiveLogsPayload) => {
  return useQuery({
    queryKey: ["live-logs", option],
    queryFn: () => lmtService.log.getLiveLog(option),
  });
};
