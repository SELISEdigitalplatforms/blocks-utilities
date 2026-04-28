import React from "react";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";

export const EmailUsageDetailsSkeleton = () => (
  <main className="flex flex-col gap-6">
    <div className="flex items-center gap-2">
      <Skeleton className="h-4 w-12" />
      <Skeleton className="h-4 w-4" />
      <Skeleton className="h-4 w-24" />
      <Skeleton className="h-4 w-4" />
      <Skeleton className="h-4 w-32" />
    </div>

    <Skeleton className="h-8 w-48" />

    <Card>
      <CardHeader>
        <Skeleton className="h-6 w-20" />
      </CardHeader>
      <CardContent className="space-y-6">
        <div className="grid grid-cols-1 gap-6 md:grid-cols-2 lg:grid-cols-4">
          <div>
            <Skeleton className="mb-2 h-3 w-10" />
            <Skeleton className="h-5 w-full max-w-[200px]" />
          </div>
          <div>
            <Skeleton className="mb-2 h-3 w-8" />
            <Skeleton className="h-5 w-full max-w-[200px]" />
          </div>
          <div>
            <Skeleton className="mb-2 h-3 w-16" />
            <Skeleton className="h-5 w-32" />
          </div>
          <div className="lg:col-span-2">
            <Skeleton className="mb-2 h-3 w-14" />
            <Skeleton className="h-5 w-full" />
          </div>
          <div>
            <Skeleton className="mb-2 h-3 w-12" />
            <Skeleton className="h-6 w-24 rounded-full" />
          </div>
        </div>

        <div>
          <Skeleton className="mb-2 h-3 w-20" />
          <Skeleton className="h-48 w-full rounded-md" />
        </div>
      </CardContent>
    </Card>
  </main>
);
