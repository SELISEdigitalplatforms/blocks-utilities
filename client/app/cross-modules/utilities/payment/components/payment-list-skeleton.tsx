import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";

export const PaymentListSkeleton = () => (
  <div aria-label="Loading payments" aria-busy="true">
    <div className="hidden overflow-hidden rounded-lg border md:block">
      <div className="grid grid-cols-[1.2fr_1fr_1fr_1fr_1fr] gap-4 border-b bg-muted/40 px-5 py-4">
        {Array.from({ length: 5 }).map((_, index) => (
          <Skeleton key={index} className="h-4 w-24" />
        ))}
      </div>
      {Array.from({ length: 6 }).map((_, row) => (
        <div
          key={row}
          className="grid grid-cols-[1.2fr_1fr_1fr_1fr_1fr] gap-4 border-b px-5 py-5 last:border-0"
        >
          <Skeleton className="h-4 w-32" />
          <Skeleton className="h-4 w-28" />
          <Skeleton className="h-4 w-24" />
          <Skeleton className="h-4 w-36" />
          <Skeleton className="h-6 w-24 rounded-full" />
        </div>
      ))}
    </div>

    <div className="grid gap-3 md:hidden">
      {Array.from({ length: 4 }).map((_, index) => (
        <div key={index} className="space-y-4 rounded-xl border p-4">
          <div className="flex items-center justify-between">
            <Skeleton className="h-4 w-32" />
            <Skeleton className="h-6 w-20 rounded-full" />
          </div>
          <Skeleton className="h-7 w-36" />
          <div className="flex items-center justify-between">
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-4 w-28" />
          </div>
        </div>
      ))}
    </div>
  </div>
);
