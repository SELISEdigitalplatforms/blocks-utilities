import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { cn } from "@/lib/utils";
import { IGetPermissionsSeverityResponse, PERMISSION_SEVERITY_OPTIONS } from "@blocks-idp/iam/models/permission";
import { useMemo } from "react";

type PermissionSeverityProps = {
  data: IGetPermissionsSeverityResponse;
  isLoading: boolean;
};

export const PermissionSeverity = ({ data, isLoading }: PermissionSeverityProps) => {
  const severityData = useMemo(() => {
    if (!data) return [];
    return PERMISSION_SEVERITY_OPTIONS.map((option) => {
      const count = data.find((item) => item.severityLevel === option.id)?.count || 0;
      return {
        ...option,
        count,
      };
    });
  }, [data]);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Permission Severity Overview</CardTitle>
      </CardHeader>
      <CardContent className="p-0">
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
          {severityData.map((item) => (
            <div key={item.value} className={cn("relative overflow-hidden px-4 py-3", item.className)}>
              <div className={cn(item.barClassName, "absolute left-0 top-0 h-full w-1.5")} />

              <div className="pl-2">
                <p className={cn("text-sm font-semibold uppercase")}>{item.label} Risk</p>

                {isLoading ? (
                  <Skeleton className="mt-3 h-8 w-[140px]" />
                ) : (
                  <div className="mt-2 flex items-end gap-2">
                    <span className="text-4xl font-extrabold leading-none text-foreground">
                      {String(item.count).padStart(2, "0")}
                    </span>
                    <span className="pb-1 text-sm font-medium text-foreground">Permissions</span>
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  );
};
