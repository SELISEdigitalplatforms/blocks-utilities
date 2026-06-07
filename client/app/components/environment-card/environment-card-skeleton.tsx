import { Card, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { ChevronRight } from "lucide-react";

export const EnvironmentCardSkeleton = () => {
  return (
    <Card className="group flex h-[60px] cursor-pointer flex-col justify-between rounded-sm p-4 shadow-none transition-shadow duration-200 hover:shadow-md">
      <CardHeader className="flex flex-row justify-between !p-0">
        <CardTitle className="line-clamp-1 break-all text-lg leading-tight">
          <div className="flex w-fit flex-row items-center gap-1">
            <Skeleton className="h-5 w-24" />
          </div>
        </CardTitle>
        <ChevronRight className="mt-1 h-4 w-4 opacity-0 transition-opacity duration-200 group-hover:opacity-100" />
      </CardHeader>
    </Card>
  );
};
