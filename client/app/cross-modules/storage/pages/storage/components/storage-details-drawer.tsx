import { X, PackageOpen } from "lucide-react";
import { IStorageConfiguration, StorageStrategyType } from "@blocks-storage/models/storage.model";
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from "@/components/ui-kits/sheet/sheet";
import { Badge } from "@/components/ui-kits/badge/badge";
import { cn, formatDate } from "@/lib/utils";

interface StorageDetailsDrawerProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  storage: IStorageConfiguration | null;
}

const providerColors: Record<StorageStrategyType, string> = {
  Amazon: "bg-orange-100 text-orange-600",
  Azure: "bg-blue-100 text-blue-600",
  SftpStorage: "bg-green-100 text-green-600",
  S3Compatible: "bg-purple-100 text-purple-600",
};

const getProviderLabel = (provider: StorageStrategyType): string => {
  switch (provider) {
    case "Amazon": return "AWS";
    case "Azure": return "Azure";
    case "SftpStorage": return "SFTP";
    case "S3Compatible": return "AWS S3 Compatible";
    default: return provider;
  }
};

export function StorageDetailsDrawer({ open, onOpenChange, storage }: StorageDetailsDrawerProps) {
  if (!storage) return null;

  const providerLabel = getProviderLabel(storage.storageStrategy);
  const providerColorClass = providerColors[storage.storageStrategy];

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full p-0 sm:max-w-md" hideClose>
        <div className="flex h-full flex-col">
          <SheetHeader className="border-b border-border px-6 py-4">
            <div className="flex items-center justify-between">
              <SheetTitle className="text-lg font-semibold">Details</SheetTitle>
              <button
                onClick={() => onOpenChange(false)}
                className="rounded-sm opacity-70 ring-offset-background transition-opacity hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
              >
                <X className="h-4 w-4" />
                <span className="sr-only">Close</span>
              </button>
            </div>
          </SheetHeader>

          <div className="flex-1 overflow-y-auto px-6 py-6">
            <div className="space-y-6">
              <div>
                <h3 className="mb-4 text-sm font-medium text-muted-foreground">Properties</h3>
                <div className="space-y-4">
                  <div>
                    <div className="mb-1 text-xs text-muted-foreground">Name</div>
                    <div className="text-sm font-medium">{storage.name}</div>
                  </div>

                  <div>
                    <div className="mb-2 text-xs text-muted-foreground">Storage provider</div>
                    <div className="flex items-center gap-2">
                      <div className={cn("flex h-8 w-8 items-center justify-center rounded", providerColorClass)}>
                        {(storage.storageStrategy === "Amazon" || storage.storageStrategy === "S3Compatible") && (
                          <img src="/assets/images/amazon.png" alt="AWS" className="h-4 w-4 object-contain" />
                        )}
                        {storage.storageStrategy === "Azure" && (
                          <img src="/assets/images/azure.png" alt="Azure" className="h-4 w-4 object-contain" />
                        )}
                        {storage.storageStrategy === "SftpStorage" && <PackageOpen className="h-4 w-4" />}
                      </div>
                      <span className="text-sm font-medium">{providerLabel}</span>
                    </div>
                  </div>

                  <div>
                    <div className="mb-1 text-xs text-muted-foreground">Owner</div>
                    <div className="text-sm font-medium">{storage.createdBy || "Me"}</div>
                  </div>

                  <div>
                    <div className="mb-2 text-xs text-muted-foreground">Type</div>
                    <Badge variant="secondary" className="h-6 w-fit text-xs font-medium">
                      Configured
                    </Badge>
                  </div>

                  <div>
                    <div className="mb-1 text-xs text-muted-foreground">Last modified</div>
                    <div className="text-sm font-medium">
                      {formatDate(new Date(storage.lastUpdatedDate))}
                    </div>
                  </div>

                  <div>
                    <div className="mb-1 text-xs text-muted-foreground">Date created</div>
                    <div className="text-sm font-medium">
                      {formatDate(new Date(storage.createdDate))}
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </SheetContent>
    </Sheet>
  );
}
