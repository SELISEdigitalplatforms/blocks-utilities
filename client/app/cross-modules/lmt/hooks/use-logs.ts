import { useCallback, useEffect, useRef, useState } from "react";
import { ILog } from "../models/log.model";
import { useProjectStore } from "@/store/useProjectStore";
import { lmtService } from "../services/lmt.service";

type UseLogsParams = {
  serviceName: string;
  pageSize?: number;
  startDate?: string;
  endDate?: string;
  search?: string;
  level?: string;
};

export const useLogs = ({
  serviceName,
  pageSize = 20,
  startDate = "",
  endDate = "",
  search = "",
  level = "",
}: UseLogsParams) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [initialLogs, setInitialLogs] = useState<ILog[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [hasTopMore, setHasTopMore] = useState<boolean>(true);
  const isFirstFetchCompleted = useRef<boolean>(false);
  const [page, setPage] = useState<number>(0);

  const generateFetchLogsPayload = useCallback(() => {
    return {
      pageSize: pageSize,
      projectKey: tenantId,
      serviceName,
      filter: {
        ...(startDate && { startDate }),
        ...(endDate && { endDate }),
        ...(level && { level }),
      },
      search,
    };
  }, [endDate, level, pageSize, search, serviceName, startDate, tenantId]);

  const fetchInitialLogs = useCallback(async () => {
    try {
      isFirstFetchCompleted.current = true;
      const res = await lmtService.log.getLogsByDate(generateFetchLogsPayload());
      setIsLoading(false);
      if (res.data.length) setInitialLogs(res.data.reverse());
      if (res.totalCount && res.totalCount <= page * pageSize) setHasTopMore(false);
    } catch (_error) {
      // Handle error
    } finally {
      setIsLoading(false);
    }
  }, [generateFetchLogsPayload, page, pageSize]);

  useEffect(() => {
    if (!isFirstFetchCompleted.current) {
      fetchInitialLogs();
    }
  }, [fetchInitialLogs]);

  const fetchOldLogs = useCallback(
    async (lastDate: string) => {
      try {
        const payload = generateFetchLogsPayload();
        payload.filter.endDate = lastDate;
        const res = await lmtService.log.getLogsByDate(payload);
        if (res.totalCount && res.totalCount <= page * pageSize) setHasTopMore(false);
        setPage((page) => page + 1);
        if (!res.data.length) return [];
        return res.data.reverse();
      } catch (_error) {
        return [];
      }
    },
    [generateFetchLogsPayload, page, pageSize],
  );

  const fetchNewLogs = useCallback(
    async (lastDate: string) => {
      try {
        if (!serviceName || !isFirstFetchCompleted.current) return [];
        const response = await lmtService.log.getLiveLog({
          serviceName: serviceName,
          projectKey: tenantId,
          lastDate: lastDate,
        });
        return response?.data.reverse() || [];
      } catch (_error) {
        return [];
      }
    },
    [serviceName, tenantId],
  );

  return { initialLogs, isLoading, hasTopMore, fetchOldLogs, fetchNewLogs };
};
