import { z } from "zod";
import { parseSSEEvent, parseSSEBuffer } from "@blocks-ai/shared/utils/parse-sse";

export { parseSSEEvent, parseSSEBuffer };

export const lmtQueryAgentSchema = z.object({
  query: z
    .string()
    .trim()
    .nonempty({ message: "Query is required" })
    .max(500, { message: "Query must not exceed 500 characters" }),
});
export type LmtQueryAgentForm = z.infer<typeof lmtQueryAgentSchema>;

export const lmtQueryAgentFormDefaultValue: LmtQueryAgentForm = {
  query: "",
};

export const generateMessageId = (type: string): string => {
  return `${type}-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
};

export const getTimestampOrNow = (timestamp: unknown): string => {
  return typeof timestamp === "string" ? timestamp : new Date().toISOString();
};

export const isValidJSON = (str: string): boolean => {
  try {
    const parsed = JSON.parse(str);
    return typeof parsed === "object" && parsed !== null;
  } catch {
    return false;
  }
};

export const formatJSON = (str: string): string => {
  try {
    return JSON.stringify(JSON.parse(str), null, 2);
  } catch {
    return str;
  }
};

const THINKING_TEXTS = [
  "Thinking…",
  "Working on it…",
  "Let me take a look…",
  "Analyzing your request…",
  "Putting things together…",
  "One moment…",
];

const FETCHING_TEXTS = [
  "Fetching relevant data…",
  "Looking things up…",
  "Gathering information…",
  "Checking our systems…",
];

const PROCESSING_TEXTS = [
  "Analyzing results…",
  "Reviewing what I found…",
  "Connecting the dots…",
  "Almost there…",
];

function pickRandom(arr: string[]) {
  return arr[Math.floor(Math.random() * arr.length)];
}

interface AgentEventCallbacks {
  showStatus: (status: string) => void;
  clearStatus: () => void;
  renderAnswer: (event: Record<string, unknown>) => void;
  showError: (error: string) => void;
}

export function handleAgentEvent(
  event: { type: string; [key: string]: unknown },
  callbacks: AgentEventCallbacks,
) {
  let status: string;
  switch (event.type) {
    case "start":
      status = pickRandom(THINKING_TEXTS);
      callbacks.showStatus(status);
      break;

    case "agent_inference_started":
      status = pickRandom(THINKING_TEXTS);
      callbacks.showStatus(status);
      break;

    case "tool_call":
      status = pickRandom(FETCHING_TEXTS);
      callbacks.showStatus(status);
      break;

    case "tool_result":
      status = pickRandom(PROCESSING_TEXTS);
      callbacks.showStatus(status);
      break;

    case "tool_error":
      status = pickRandom(THINKING_TEXTS);
      callbacks.showStatus(status);
      break;

    case "error":
      callbacks.showError(event.error as string);
      break;

    case "final_answer":
      callbacks.clearStatus();
      callbacks.renderAnswer(event);
      break;
  }
}
