export const parseSSEEvent = (
  line: string,
): { eventType: string; eventData: Record<string, unknown> } | null => {
  const eventMatch = line.match(/^event:\s*(.+)$/m);
  const dataMatch = line.match(/^data:\s*(.+)$/m);
  if (!eventMatch || !dataMatch) return null;
  try {
    return {
      eventType: eventMatch[1].trim(),
      eventData: JSON.parse(dataMatch[1].trim()),
    };
  } catch {
    return null;
  }
};

export const parseSSEBuffer = (
  buffer: string,
): {
  events: Array<{ eventType: string; eventData: Record<string, unknown> }>;
  remaining: string;
} => {
  const separator = buffer.includes("\r\n\r\n") ? "\r\n\r\n" : "\n\n";
  const parts = buffer.split(separator);
  const remaining = parts.pop() || "";
  const events = parts
    .map((line) => parseSSEEvent(line))
    .filter((event): event is NonNullable<typeof event> => event !== null);
  return { events, remaining };
};
