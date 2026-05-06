import { useParams, useNavigate } from "react-router-dom";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { Button } from "@/components/ui-kits/button/button";
import { CircleSlash } from "lucide-react";
import { useGetMagicUrlById } from "@blocks-utilities/magic-url/hooks/use-magic-url";
import { useDeactivateMagicUrl } from "@blocks-utilities/magic-url/hooks/use-deactivate-magic-url";
import { useProjectStore } from "@/store/useProjectStore";
import { MagicUrlStatusBadge } from "@blocks-utilities/magic-url/pages/magic-url-status-badge";
import { Progress } from "@/components/ui-kits/progress/progress";
import { formatDate, parseDateString } from "@/lib/utils";
import { useState } from "react";

export default function MagicUrlDetailsPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  const { data: magicUrl, isLoading } = useGetMagicUrlById({
    ItemId: id!,
    projectKey: tenantId,
  });
  const { deactivateMagicUrl, isRemoving } = useDeactivateMagicUrl();
  const [isDeactivateModalOpen, setIsDeactivateModalOpen] = useState(false);

  const handleDeactivate = () => {
    if (id) {
      deactivateMagicUrl(id, tenantId, () => {
        setIsDeactivateModalOpen(false);
        navigate("/magic-url");
      });
    }
  };

  const usagePercent = magicUrl?.usageLimit
    ? Math.min((magicUrl.usageCount / magicUrl.usageLimit) * 100, 100)
    : 0;

  return (
    <div className="flex flex-col gap-6 p-6">
      <PageBreadcrumb breadcrumbIndex={2} />
      {isLoading ? (
        <Card>
          <CardContent className="flex flex-col gap-4 p-6">
            <Skeleton className="h-8 w-64" />
            <Skeleton className="h-32 w-full" />
            <Skeleton className="h-32 w-full" />
          </CardContent>
        </Card>
      ) : magicUrl ? (
        <Card>
          <CardHeader>
            <div className="flex flex-row items-center justify-between">
              <CardTitle>{magicUrl.name || magicUrl.uri}</CardTitle>
              <Dialog
                open={isDeactivateModalOpen}
                onOpenChange={setIsDeactivateModalOpen}
              >
                <Button
                  variant="outline"
                  size="sm"
                  className="gap-2 text-red-500 hover:bg-red-400 hover:text-white"
                  onClick={() => setIsDeactivateModalOpen(true)}
                >
                  <CircleSlash className="h-4 w-4" />
                  Deactivate
                </Button>
                <ConfirmationModal
                  onCancel={() => setIsDeactivateModalOpen(false)}
                  onConfirm={handleDeactivate}
                  data={{
                    dialogTitle: "Deactivate Magic URL",
                    dialogSubtitle:
                      "Are you sure you want to deactivate this Magic URL? This action cannot be undone.",
                    confirmButton: isRemoving
                      ? "Deactivating..."
                      : "Deactivate",
                    cancelButton: "Cancel",
                  }}
                  buttonState={{ confirm: { disable: isRemoving } }}
                />
              </Dialog>
            </div>
          </CardHeader>
          <CardContent className="flex flex-col gap-6">
            <div className="grid grid-cols-1 gap-6 md:grid-cols-3">
              <div>
                <p className="mb-2 text-sm text-muted-foreground">
                  Total Usage
                </p>
                <div className="flex items-center gap-3">
                  <span className="text-lg font-medium">
                    {magicUrl.usageCount} /{" "}
                    {magicUrl.usageLimit === 0
                      ? "Unlimited"
                      : magicUrl.usageLimit}
                  </span>
                  {magicUrl.usageLimit > 0 && (
                    <Progress value={usagePercent} className="h-2 flex-1" />
                  )}
                </div>
              </div>
              <div>
                <p className="mb-2 text-sm text-muted-foreground">Status</p>
                <div className="mt-1 w-fit">
                  <MagicUrlStatusBadge item={magicUrl} />
                </div>
              </div>
              <div>
                <p className="mb-2 text-sm text-muted-foreground">Created By</p>
                <p className="text-base">{magicUrl.createdBy || "-"}</p>
              </div>
            </div>

            <div className="grid grid-cols-1 gap-6 md:grid-cols-3">
              <div>
                <p className="mb-2 text-sm text-muted-foreground">Created On</p>
                <p className="text-base">
                  {formatDate(parseDateString(magicUrl.createdAt))}
                </p>
              </div>
              <div>
                <p className="mb-2 text-sm text-muted-foreground">
                  Scheduled Expiry Date
                </p>
                <p className="text-base">
                  {magicUrl.expiryDate
                    ? formatDate(parseDateString(magicUrl.expiryDate))
                    : "-"}
                </p>
              </div>
              <div>
                <p className="mb-2 text-sm text-muted-foreground">
                  Expiry Reason
                </p>
                <p className="text-base">{magicUrl.expiredReason || "-"}</p>
              </div>
            </div>

            <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
              <div>
                <p className="mb-2 text-sm text-muted-foreground">
                  Shortened URL
                </p>
                <CopyToClipboardButton textToCopy={magicUrl.shortUri}>
                  <span className="max-w-[400px] truncate font-mono text-sm">
                    {magicUrl.shortUri}
                  </span>
                </CopyToClipboardButton>
              </div>
              <div>
                <p className="mb-2 text-sm text-muted-foreground">
                  Original URL
                </p>
                <CopyToClipboardButton textToCopy={magicUrl.uri}>
                  <span className="max-w-[400px] truncate font-mono text-sm">
                    {magicUrl.uri}
                  </span>
                </CopyToClipboardButton>
              </div>
            </div>

            <div className="grid grid-cols-1 gap-6 md:grid-cols-3">
              <div>
                <p className="mb-2 text-sm text-muted-foreground">
                  Request Method
                </p>
                <p className="text-base uppercase">
                  {magicUrl.requestMethod || "-"}
                </p>
              </div>
              <div>
                <p className="mb-2 text-sm text-muted-foreground">
                  Client Credential
                </p>
                <p className="max-w-[200px] truncate text-base">
                  {magicUrl.clientCredential || "-"}
                </p>
              </div>
              <div>
                <p className="mb-2 text-sm text-muted-foreground">Type</p>
                <p className="text-base">{magicUrl.type || "-"}</p>
              </div>
            </div>
          </CardContent>
        </Card>
      ) : (
        <Card>
          <CardContent className="flex h-32 items-center justify-center text-sm text-muted-foreground">
            Magic URL not found.
          </CardContent>
        </Card>
      )}
    </div>
  );
}
