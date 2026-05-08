import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";

export function MagicUrlDetailsSkeleton() {
  return (
    <main>
      <div className="space-y-6">
        {/* Breadcrumb skeleton */}
        <Skeleton className="h-5 w-64" />

        {/* Title skeleton */}
        <Skeleton className="h-8 w-48" />

        {/* Card skeleton */}
        <Card>
          <CardHeader className="flex flex-row items-center justify-between">
            <Skeleton className="h-6 w-32" />
            <Skeleton className="h-9 w-20" />
          </CardHeader>
          <CardContent className="grid gap-6">
            {/* Row 1 */}
            <div className="grid grid-cols-1 gap-4 md:grid-cols-4">
              <div className="col-span-1 space-y-2">
                <Skeleton className="h-4 w-24" />
                <Skeleton className="h-5 w-full" />
              </div>
              <div className="col-span-1 space-y-2">
                <Skeleton className="h-4 w-16" />
                <Skeleton className="h-6 w-20" />
              </div>
              <div className="space-y-2">
                <Skeleton className="h-4 w-20" />
                <Skeleton className="h-5 w-32" />
              </div>
            </div>

            {/* Row 2 */}
            <div className="grid grid-cols-1 gap-4 md:grid-cols-4">
              <div className="space-y-2">
                <Skeleton className="h-4 w-24" />
                <Skeleton className="h-5 w-32" />
              </div>
              <div className="space-y-2">
                <Skeleton className="h-4 w-40" />
                <Skeleton className="h-5 w-32" />
              </div>
            </div>

            {/* Row 3 */}
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <div className="space-y-2">
                <Skeleton className="h-4 w-28" />
                <Skeleton className="h-5 w-full" />
              </div>
              <div className="space-y-2">
                <Skeleton className="h-4 w-12" />
                <Skeleton className="h-5 w-full" />
              </div>
            </div>
          </CardContent>
        </Card>
      </div>
    </main>
  );
}
