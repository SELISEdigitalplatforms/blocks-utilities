import { http } from "@/lib/http-client";
import { API_BASES } from "@/constants/endpoint.constant";
import { AI_ENDPOINTS } from "@blocks-ai/constants/endpoint.constant";
import { ILMTQueryAgentPayload } from "@blocks-ai/types/agent.service.type";

class AgentService {
  async lmtQuerySSE(payload: ILMTQueryAgentPayload): Promise<ReadableStream<Uint8Array>> {
    return http.stream(`${API_BASES.AI}${AI_ENDPOINTS.AGENT_QUERY_LMT_STREAM}`, payload, {
      Accept: "text/event-stream",
    });
  }
}

export const agentService = new AgentService();
