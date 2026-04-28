import { http } from "@/lib/http-client";
import {
  IGetTraceByTraceIdPayload,
  IGetTracesPayload,
  IGetTracesResponse,
  Trace,
  TraceTree,
  ITags,
  ISecurityContext,
  IRequest,
  IResponse,
} from "../models/trace.model";
import { IAPIResponse } from "@/models/api-response";
import { TRACE_ENDPOINTS } from "../constants/endpoint.constant";

export class TraceService {
  async getTraces(payload: IGetTracesPayload): Promise<IGetTracesResponse> {
    try {
      const response = await http.post<IAPIResponse<Trace[]>>(TRACE_ENDPOINTS.GET_TRACES, payload);

      const parsedData: TraceTree[] = response.data.map((trace) => ({
        ...trace,
        id: trace.traceId,
        entryPoint: {
          method: trace.operationName.split(" ")[0],
          actionName: trace.operationName.split(" ")[1] || trace.operationName,
        },
        issues: [],
        service: trace.serviceName,
        tags: {} as ITags,
        securityContext: {} as ISecurityContext,
        request: {} as IRequest,
        response: {} as IResponse,
        logs: [],
        subEntries: [],
      }));

      return {
        data: parsedData,
        errors: response.errors ?? [],
        totalCount: response.totalCount ?? 0,
      };
    } catch (error) {
      console.error("Failed to fetch traces:", error);
      throw error;
    }
  }

  async getTraceByTraceId({
    traceId,
    projectKey,
  }: IGetTraceByTraceIdPayload): Promise<IAPIResponse<TraceTree>> {
    try {
      const response = await http.get<IAPIResponse<Trace[]>>(
        `${TRACE_ENDPOINTS.GET_TRACE}?TraceId=${traceId}&ProjectKey=${projectKey}`,
      );

      const map: Record<string, TraceTree> = {};

      const time = {
        start: "",
        end: "",
      };

      const parsedData = response.data
        .map((item) => {
          map[item.spanId] = {
            ...item,
            subEntries: [] as TraceTree[],
            issues: [],
            tags: {} as ITags,
            securityContext: {} as ISecurityContext,
            request: {} as IRequest,
            response: {} as IResponse,
            logs: [],
            entryPoint: {
              method: item.operationName.split(" ")[0],
              actionName: item.operationName.split(" ")[1] || item.operationName,
            },
          };
          if (!item.parentId) {
            time.start = item.startTime;
            time.end = item.endTime;
          }
          return item;
        })
        .reduce((acc: TraceTree | null, item) => {
          if (new Date(time.start) > new Date(item.startTime)) {
            time.start = item.startTime;
          }
          if (new Date(time.end) < new Date(item.endTime)) {
            time.end = item.endTime;
          }
          if (item.parentId === "") {
            acc = map[item.spanId];
          } else {
            // eslint-disable-next-line @typescript-eslint/ban-ts-comment
            // @ts-expect-error
            map[item.parentSpanId].subEntries.push(map[item.spanId]);
          }
          return acc;
        }, null);

      if (parsedData) {
        parsedData.calculatedStartTime = time.start;
        parsedData.calculatedEndTime = time.end;
        parsedData.calculatedDuration = Number(new Date(time.end)) - Number(new Date(time.start));
      }

      return {
        data: parsedData as TraceTree,
        errors: [],
        totalCount: 0,
      };
    } catch (error) {
      console.error("Failed to fetch logs:", error);
      throw error;
    }
  }
}
