export const LOG_LEVEL = {
  Information: "Information",
  Warning: "Warning",
  Error: "Error",
};

export * from "./usage.util";

export const getLogFormatTimestamp = (timestamp: string) => {
  const date = new Date(timestamp);
  if (isNaN(date.getTime())) {
    return timestamp;
  }
  return date.toISOString().replace("T", " ").replace("Z", "").slice(0, -4);
};

export const getLogLevelClassName = (level: string) => {
  switch (level) {
    case "Warning":
      return "text-warning";
    case "Information":
      return "text-success";
    case "Error":
      return "text-error";
    default:
      return "text-high-emphasis";
  }
};
