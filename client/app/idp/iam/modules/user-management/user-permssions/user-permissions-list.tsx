
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { IPermission } from "@blocks-idp/iam/models/permission";
import { X } from "lucide-react";

interface UserPermissionsListProps {
  permissions: IPermission[];
  isLoading: boolean;
  userId: string;
  onRemovePermission: (resource: string) => void;
}
const LoadingSkelton = () => (
  <div className="grid w-full grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
    {Array.from({ length: 5 }).map((_, index) => (
      <div key={index} className="col-span-1 flex items-center py-2">
        <div className="flex w-full items-center rounded-2xl border border-border px-4 py-2 shadow-sm">
          <div className="flex min-w-0 flex-1 flex-col space-y-1">
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-3 w-16" />
          </div>
          <div className="ml-2 flex items-center">
            <Skeleton className="h-7 w-7 rounded-md" />
          </div>
        </div>
      </div>
    ))}
  </div>
);

export const UserPermissionsList = ({
  permissions,
  isLoading,
  userId,
  onRemovePermission,
}: UserPermissionsListProps) => {
  if (isLoading) {
    return <LoadingSkelton />;
  }

  return (
    <>
      {permissions && permissions.length > 0 ? (
        <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
          {permissions.map((item) => (
            <div key={item.itemId} className="col-span-1 flex items-center py-2">
              <div className="flex w-full items-center rounded-2xl border border-border px-4 py-2 shadow-sm">
                <div className="flex min-w-0 flex-1 flex-col">
                  <span className="truncate text-base font-medium leading-tight" title={item.name}>
                    {item.name}
                  </span>
                  <span
                    className="truncate text-xs lowercase text-muted-foreground"
                    title={item.resource}
                  >
                    {item.resource}
                  </span>
                </div>
                <div className="ml-2 flex items-center">
                  <button
                    type="button"
                    className="flex h-7 w-7 items-center justify-center rounded-md bg-secondary transition-colors hover:bg-secondary/80"
                    onClick={() => onRemovePermission(item.resource)}
                    aria-label="Remove role"
                  >
                    <X className="h-4 w-4" />
                  </button>
                </div>
              </div>
            </div>
          ))}
        </div>
      ) : (
        <div className="flex h-24 items-center justify-center text-muted-foreground">
          No permission found
        </div>
      )}
    </>
  );
};
