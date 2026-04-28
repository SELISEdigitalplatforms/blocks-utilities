import { agentService } from "@blocks-ai/services/agent.service";

export const useLMTQueryAgentSSE = () => {
  return {
    streamQuery: agentService.lmtQuerySSE.bind(agentService),
  };
};
