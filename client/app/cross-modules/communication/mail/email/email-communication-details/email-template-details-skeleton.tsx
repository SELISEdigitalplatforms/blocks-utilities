import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { CardHeader, CardContent } from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { ArrowLeft } from "lucide-react";

export const EmailTemplateDetailsSkeleton = () => {
  return (
    <div>
      <div className="mb-5 hidden md:flex">
        <Skeleton className="h-4 w-64" />
      </div>
      <div className="mt-5 flex items-center justify-between">
        <div className="item-center flex gap-2">
          <Button size="icon" variant="ghost" className="h-8 w-8" disabled>
            <ArrowLeft className="h-6 w-6" />
          </Button>
          <Skeleton className="h-8 w-64" />
        </div>
        <div className="flex gap-4">
          <Skeleton className="h-10 w-32" />
        </div>
      </div>
      <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-3">
        {/* Template Section */}
        <div className="rounded-sm border border-gray-200 bg-white shadow-none dark:border-gray-700 dark:bg-gray-800 lg:col-span-2">
          <CardHeader>
            <div className="flex w-full items-center justify-between px-4 pt-4">
              <Skeleton className="h-7 w-24" />
              <Skeleton className="h-10 w-20" />
            </div>
          </CardHeader>
          <CardContent>
            <div className="mt-4 grid h-[60vh] w-full animate-pulse gap-1 border-t bg-gray-100 dark:bg-gray-900"></div>
          </CardContent>
        </div>

        {/* Details Section */}
        <div className="rounded-sm border border-gray-200 bg-white shadow-none dark:border-gray-700 dark:bg-gray-800">
          <CardHeader>
            <div className="flex w-full items-center justify-between px-4 pt-4">
              <Skeleton className="h-7 w-20" />
              <Skeleton className="h-10 w-20" />
            </div>
          </CardHeader>

          <CardContent>
            <div className="border-t px-4 pt-4">
              <div className="mb-10 space-y-2">
                <Skeleton className="h-4 w-16" />
                <Skeleton className="h-5 w-full" />
              </div>
            </div>
            <div className="grid grid-cols-2 gap-4 px-4">
              <div className="grid gap-10">
                <div className="space-y-2">
                  <Skeleton className="h-4 w-20" />
                  <Skeleton className="h-5 w-24" />
                </div>
                <div className="space-y-2">
                  <Skeleton className="h-4 w-20" />
                  <Skeleton className="h-5 w-24" />
                </div>
              </div>
              <div className="grid gap-10">
                <div className="space-y-2">
                  <Skeleton className="h-4 w-24" />
                  <Skeleton className="h-5 w-24" />
                </div>
                <div className="space-y-2">
                  <Skeleton className="h-4 w-24" />
                  <Skeleton className="h-5 w-24" />
                </div>
              </div>
            </div>
          </CardContent>
        </div>
      </div>
    </div>
  );
};
