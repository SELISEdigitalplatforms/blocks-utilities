import { LucideIcon, AlertCircle } from "lucide-react";
import { cn } from "@/lib/utils";

interface ErrorDisplayProps {
  icon?: LucideIcon;
  text?: string;
  className?: string;
  iconClassName?: string;
  textClassName?: string;
}

export function ErrorDisplay({
  icon: Icon = AlertCircle,
  text,
  className,
  iconClassName,
  textClassName,
}: ErrorDisplayProps) {
  return (
    <div
      className={cn(
        "flex flex-col items-center justify-center gap-2",
        className,
      )}>
      <Icon className={cn("h-8 w-8 text-destructive", iconClassName)} />
      {text && (
        <p className={cn("text-sm text-muted-foreground", textClassName)}>
          {text}
        </p>
      )}
    </div>
  );
}
