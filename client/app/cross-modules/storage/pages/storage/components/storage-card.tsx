import { Card, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { MoreVertical, PackageOpen, Info } from "lucide-react";
import { cn } from "@/lib/utils";
import { StorageStrategyType } from "@blocks-storage/models/storage.model";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";

export interface StorageCardData {
  id: string;
  provider: StorageStrategyType;
  providerIcon: string;
  providerColor: string;
  title: string;
  subtitle: string;
}

type StorageCardProps = {
  data: StorageCardData;
  onClick?: (id: string) => void;
  onViewDetails?: (id: string) => void;
  onRemove?: (id: string) => void;
  onDisconnect?: (id: string) => void;
};

const providerColors: Record<StorageStrategyType, string> = {
  Amazon: "bg-orange-100 text-orange-600",
  Azure: "bg-blue-100 text-blue-600",
  SftpStorage: "bg-green-100 text-green-600",
  S3Compatible: "bg-purple-100 text-purple-600",
};

export const StorageCard = ({
  data,
  onClick,
  onViewDetails,
}: StorageCardProps) => {
  const handleClick = () => {
    onClick?.(data.id);
  };

  const handleViewDetails = (e: React.MouseEvent) => {
    e.stopPropagation();
    onViewDetails?.(data.id);
  };

  const providerColorClass = providerColors[data.provider];

  return (
    <Card
      onClick={handleClick}
      className={cn(
        "cursor-pointer shadow-none transition-shadow duration-200 hover:shadow-md",
        "relative flex h-full flex-col",
      )}
    >
      <CardHeader className="flex-row items-start justify-between space-y-0">
        <div className="flex flex-1 items-center gap-3">
          <div
            className={cn(
              "flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-md",
              providerColorClass,
            )}
          >
            {(data.provider === "Amazon" || data.provider === "S3Compatible") && (
              <img
                src="/assets/images/amazon.png"
                alt="AWS"
                className="h-5 w-5 object-contain"
              />
            )}
            {data.provider === "Azure" && (
              <img
                src="/assets/images/azure.png"
                alt="Azure"
                className="h-5 w-5 object-contain"
              />
            )}
            {data.provider === "SftpStorage" && <PackageOpen className="h-5 w-5" />}
          </div>
          <div className="min-w-0 flex-1">
            <CardTitle className="line-clamp-2 text-base font-semibold leading-tight">
              {data.title}
            </CardTitle>
            <p className="text-sm text-muted-foreground">{data.subtitle}</p>
          </div>
        </div>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button
              onClick={(e) => e.stopPropagation()}
              className="flex-shrink-0 text-muted-foreground hover:text-foreground"
            >
              <MoreVertical className="h-5 w-5" />
            </button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" onClick={(e) => e.stopPropagation()}>
            <DropdownMenuItem onClick={handleViewDetails} className="cursor-pointer">
              <Info className="mr-2 h-4 w-4" />
              View Details
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </CardHeader>
    </Card>
  );
};
