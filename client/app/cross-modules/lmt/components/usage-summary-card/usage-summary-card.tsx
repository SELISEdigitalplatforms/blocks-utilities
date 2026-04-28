import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { cn } from "@/lib/utils";
import { ElementType } from "react";

type UsageSummaryCardProps = {
  title: string;
  description: string;
  className?: string;
  isLoading?: boolean;
  Icon: ElementType;
};

const UsageSummaryCardSkelton = () => (
  <Card className="border-none p-0 shadow-none">
    <CardContent className="flex items-center gap-3">
      <Skeleton className="aspect-square w-14" />
      <div className="flex-1">
        <Skeleton className="h-7 w-1/2" />
        <Skeleton className="mt-1 h-5 w-full" />
      </div>
    </CardContent>
  </Card>
);

export const UsageSummaryCard = ({
  title,
  description,
  className,
  isLoading,
  Icon,
}: UsageSummaryCardProps) => {
  if (isLoading) return <UsageSummaryCardSkelton />;
  return (
    <Card className="border-none p-0 shadow-none">
      <CardContent className="flex items-center gap-3">
        <div className={cn("rounded-sm bg-blocks-primary-25 p-3 text-primary", className)}>
          <Icon className="aspect-square w-8" />
        </div>
        <div>
          <p className="text-2xl font-semibold text-high-emphasis">{title}</p>
          <p className="text-sm font-medium text-medium-emphasis">{description}</p>
        </div>
      </CardContent>
    </Card>
  );
};
