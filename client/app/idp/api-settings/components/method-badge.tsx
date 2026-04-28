import { cn } from "@/lib/utils";

const METHOD_STYLES: Record<string, string> = {
  GET: "bg-emerald-500/10 text-emerald-600 ring-1 ring-emerald-500/40 dark:text-emerald-400 dark:ring-emerald-400/30",
  POST: "bg-blue-500/10 text-blue-600 ring-1 ring-blue-500/40 dark:text-blue-400 dark:ring-blue-400/30",
  PUT: "bg-amber-500/10 text-amber-600 ring-1 ring-amber-500/40 dark:text-amber-400 dark:ring-amber-400/30",
  PATCH: "bg-purple-500/10 text-purple-600 ring-1 ring-purple-500/40 dark:text-purple-400 dark:ring-purple-400/30",
  DELETE: "bg-red-500/10 text-red-600 ring-1 ring-red-500/40 dark:text-red-400 dark:ring-red-400/30",
};

const FALLBACK_STYLE = "bg-muted text-muted-foreground ring-1 ring-border";

type MethodBadgeProps = {
  method?: string;
};

export const MethodBadge = ({ method }: MethodBadgeProps) => {
  const upper = (method ?? "").toUpperCase();
  return (
    <span
      className={cn(
        "inline-flex min-w-[52px] shrink-0 items-center justify-center rounded-md px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider",
        METHOD_STYLES[upper] || FALLBACK_STYLE,
      )}
    >
      {upper}
    </span>
  );
};
