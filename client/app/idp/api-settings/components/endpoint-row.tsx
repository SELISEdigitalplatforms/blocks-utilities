import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Switch } from "@/components/ui-kits/switch/switch";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Lock } from "lucide-react";
import { MethodBadge } from "./method-badge";
import { IApiEndpoint } from "../models/api-endpoint.model";

type EndpointRowProps = {
  endpoint: IApiEndpoint;
  isSelected: boolean;
  onSelect: (id: string, checked: boolean) => void;
  onToggleMfa: (endpoint: IApiEndpoint, value: boolean) => void;
  onToggleCaptcha: (endpoint: IApiEndpoint, value: boolean) => void;
};

export const EndpointRow = ({
  endpoint,
  isSelected,
  onSelect,
  onToggleMfa,
  onToggleCaptcha,
}: EndpointRowProps) => {
  const isCritical = (endpoint.method ?? "").toUpperCase() === "DELETE";

  return (
    <div className="group flex flex-col gap-2.5 rounded-lg border border-border bg-background px-3 py-3 transition-colors hover:bg-accent/20 sm:flex-row sm:items-center sm:justify-between sm:px-4">
      {/* Left: checkbox + method badge + path + description */}
      <div className="flex min-w-0 flex-1 items-start gap-2.5">
        <Checkbox
          checked={isSelected}
          onCheckedChange={(checked) => onSelect(endpoint.itemId, !!checked)}
          className="mt-0.5 shrink-0 sm:mt-0"
        />
        <MethodBadge method={endpoint.method} />
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-1.5">
            <code className="break-all rounded-md bg-muted px-2 py-0.5 text-xs font-mono font-medium leading-relaxed">
              /{endpoint.controller}/{endpoint.method.charAt(0).toUpperCase() + endpoint.method.slice(1)}
            </code>
            {isCritical && (
              <Badge
                variant="error"
                className="shrink-0 rounded-full px-2 text-[10px] uppercase tracking-wide"
              >
                Critical
              </Badge>
            )}
          </div>
          {endpoint.description && (
            <p className="mt-1 text-[11px] leading-snug text-muted-foreground line-clamp-1">
              {endpoint.description}
            </p>
          )}
        </div>
      </div>

      {/* Right: MFA + Captcha toggles */}
      <div className="flex shrink-0 items-center gap-3 pl-[52px] sm:gap-4 sm:pl-0">
        <div className="flex items-center gap-1.5">
          <Switch
            size="sm"
            checked={endpoint.isMFARequired}
            onCheckedChange={(val) => onToggleMfa(endpoint, val)}
          />
          <span className="text-[11px] font-medium text-muted-foreground">MFA</span>
          <span style={{ display: "inline-block", width: 16, height: 16 }}>
            <Lock className={
              `h-3 w-3 transition-colors ${endpoint.isMFARequired ? "text-amber-500" : "text-border"}`
            } />
          </span>
        </div>

        <div className="h-3.5 w-px bg-border" />

        <div className="flex items-center gap-1.5">
          <Switch
            size="sm"
            checked={endpoint.isCaptchaRequired}
            onCheckedChange={(val) => onToggleCaptcha(endpoint, val)}
          />
          <span className="text-[11px] font-medium text-muted-foreground">Captcha</span>
        </div>
      </div>
    </div>
  );
};
