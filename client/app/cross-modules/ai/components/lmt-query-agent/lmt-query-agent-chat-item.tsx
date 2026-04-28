import { isValidJSON, formatJSON } from "./utils";

type LmtQueryAgentChatItemProps = {
  time: string;
  message: string;
  type: "human" | "bot";
};

const formatChatTimestamp = (timestamp?: string) => {
  if (!timestamp) return "";
  const date = new Date(timestamp);
  return date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
};

export const LMTQueryAgentChatItem = ({ message, type, time }: LmtQueryAgentChatItemProps) => {
  if (type === "human") {
    return (
      <div className="flex flex-col items-end gap-1">
        <span className="text-xs text-low-emphasis">{formatChatTimestamp(time)}</span>
        <div className="max-w-[80%] rounded-2xl rounded-tr-sm bg-primary px-4 py-2.5 text-sm text-primary-foreground">
          <p>{message}</p>
        </div>
      </div>
    );
  }

  if (isValidJSON(message)) {
    const formattedJson = formatJSON(message);
    return (
      <div className="flex flex-col">
        <span className="text-xs text-low-emphasis">{formatChatTimestamp(time)}</span>
        <pre className="my-2 overflow-auto rounded-md border bg-muted p-3 font-mono text-sm">
          {formattedJson}
        </pre>
      </div>
    );
  }

  return (
    <div className="flex flex-col">
      <span className="text-xs text-low-emphasis">{formatChatTimestamp(time)}</span>
      <div className="my-0 py-0 text-sm text-high-emphasis whitespace-pre-wrap">{message}</div>
    </div>
  );
};
