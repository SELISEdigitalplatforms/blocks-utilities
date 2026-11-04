import { useState } from "react";
import { useParams, useNavigate } from "react-router";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { CircleSlash, EllipsisVertical } from "lucide-react";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { Button } from "@/components/ui-kits/button/button";
import { useGetMagicUrlById } from "@blocks-utilities/magic-url/hooks/use-magic-url";
import { useDeactivateMagicUrl } from "@blocks-utilities/magic-url/hooks/use-deactivate-magic-url";
import { useProjectStore, useT } from "@seliseblocks/genesis-os";
import { useScopedPath } from "@seliseblocks/genesis-os/hooks";
import { MagicUrlStatusBadge } from "@blocks-utilities/magic-url/pages/magic-url-status-badge";
import { Progress } from "@/components/ui-kits/progress/progress";
import { formatDate, parseDateString } from "@/lib/utils";
import { useDynamicBreadcrumbLabel } from "@/contexts/breadcrumb-context";
import { useGetCreator } from "@/cross-modules/utilities/magic-url/hooks/use-user-details";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { MagicUrl, MagicUrlDetailsSkeleton } from "@blocks-utilities/magic-url";
import { toast } from "@/hooks/use-toast";

export default function MagicUrlDetailsPage() {
  const { t } = useT("utilities.console");
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  const {
    data: magicUrl,
    isLoading,
    isError,
  } = useGetMagicUrlById({
    ItemId: id!,
    projectKey: tenantId,
  });
  const { deactivateMagicUrl, isRemoving } = useDeactivateMagicUrl();
  const [isDeactivateModalOpen, setIsDeactivateModalOpen] = useState(false);

  const magicUrlHref = scoped(`magic-url/details/${id}`);
  const magicUrlName = magicUrl?.name || "";
  useDynamicBreadcrumbLabel(magicUrlHref, magicUrlName);

  const createdBy = magicUrl?.createdBy;
  const { data: creatorData } = useGetCreator(createdBy, tenantId);
  const createdByName = creatorData?.data
    ? `${creatorData.data.firstName} ${creatorData.data.lastName}`
    : createdBy;

  if (isLoading || !tenantId) {
    return <MagicUrlDetailsSkeleton />;
  }

  if (isError) {
    return <div>{t("MAGIC_URL_DETAILS.ERROR_LOADING_DETAILS")}</div>;
  }

  if (!magicUrl) {
    return <div>{t("MAGIC_URL_DETAILS.DETAILS_NOT_FOUND")}</div>;
  }

  const handleDeactivate = (item: MagicUrl, event: React.MouseEvent) => {
    event.stopPropagation();
    setIsDeactivateModalOpen(true);
  };
  const confirmDeactivate = () => {
    if (!tenantId) {
      toast({
        variant: "destructive",
        title: t("common:ERROR"),
        description: t("MAGIC_URL_DETAILS.NO_PROJECT_SELECTED_PLEASE_SELECT"),
      });
      setIsDeactivateModalOpen(false);
      return;
    }
    if (magicUrl) {
      deactivateMagicUrl(magicUrl.itemId, tenantId, () => {
        setIsDeactivateModalOpen(false);
        navigate(scoped("magic-url"));
      });
    }
  };

  return (
    <div className="flex flex-col gap-6 p-6">
      <PageBreadcrumb breadcrumbIndex={3} />
      <div className="mb-6 mt-4 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">{magicUrl?.name}</h1>
        <DropdownMenu modal={false}>
          <DropdownMenuTrigger asChild>
            <Button variant="outline" className="h-8 w-8 p-0">
              <EllipsisVertical className="h-4 w-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem
              className="cursor-pointer text-error"
              onClick={(e) => {
                magicUrl && handleDeactivate(magicUrl, e);
                e.preventDefault();
              }}
            >
              <CircleSlash className="mr-2 h-4 w-4" />
              <span>{t("MAGIC_URL_DETAILS.DEACTIVATE")}</span>
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
      <Card>
        <CardHeader className="flex flex-row items-center justify-between">
          <CardTitle>{t("common:DETAILS")}</CardTitle>
        </CardHeader>
        <CardContent className="grid gap-6 p-4">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-4">
            <div className="col-span-1">
              <p className="text-sm text-muted-foreground">{t("MAGIC_URL_DETAILS.TOTAL_USAGE")}</p>
              {magicUrl.usageLimit > 0 ? (
                <div className="mt-2 flex items-center gap-2">
                  <Progress
                    value={
                      magicUrl.usageLimit > 0
                        ? (magicUrl.usageCount / magicUrl.usageLimit) * 100
                        : 0
                    }
                    className="h-5 rounded-md"
                    indicatorClassName="bg-gradient-to-r from-[#8DF0AE] to-[#17C964]"
                  />
                  <span className="whitespace-nowrap text-sm font-medium">
                    {Math.round(
                      magicUrl.usageLimit > 0
                        ? (magicUrl.usageCount / magicUrl.usageLimit) * 100
                        : 0,
                    )}
                    % ({magicUrl.usageCount}/{magicUrl.usageLimit})
                  </span>
                </div>
              ) : (
                <div className="mt-1 text-sm font-medium">
                  {magicUrl.usageCount}
                </div>
              )}
            </div>
            <div className="col-span-1">
              <p className="text-sm text-muted-foreground">{t("common:STATUS")}</p>
              <div className="mt-1 w-fit">
                <MagicUrlStatusBadge item={magicUrl} />
              </div>
            </div>
            <div className="space-y-2">
              <span className="text-sm font-medium text-muted-foreground">
                {t("MAGIC_URL_DETAILS.CREATED_BY")}
              </span>
              <div className="text-sm font-medium">{createdByName ?? "-"}</div>
            </div>
          </div>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-4">
            <div className="space-y-1">
              <span className="text-sm font-medium text-muted-foreground">
                {t("MAGIC_URL_DETAILS.CREATED_ON")}
              </span>
              <div className="text-sm font-medium">
                {formatDate(parseDateString(magicUrl.createdAt))}
              </div>
            </div>
            <div className="space-y-1">
              <span className="text-sm font-medium text-muted-foreground">
                {t("MAGIC_URL_DETAILS.SCHEDULED_EXPIRY_DATE")}
              </span>
              <div className="text-sm font-medium">
                {magicUrl.expiryDate
                  ? formatDate(parseDateString(magicUrl.expiryDate))
                  : "-"}
              </div>
            </div>
            {magicUrl.expiredReason && (
              <div className="space-y-1">
                <span className="text-sm font-medium text-muted-foreground">
                  {t("MAGIC_URL_DETAILS.EXPIRY_REASON")}
                </span>
                <div className="text-sm font-medium">
                  {magicUrl.expiredReason}
                </div>
              </div>
            )}
          </div>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <div className="space-y-1">
              <span className="text-sm font-medium text-muted-foreground">
                {t("MAGIC_URL_DETAILS.SHORTENED_URL")}
              </span>
              <CopyToClipboardButton textToCopy={magicUrl.shortUri} isHoverable>
                <div className="min-w-0 truncate text-sm font-medium">
                  {magicUrl.shortUri}
                </div>
              </CopyToClipboardButton>
            </div>
            <div className="space-y-1">
              <span className="text-sm font-medium text-muted-foreground">
                {t("MAGIC_URL_DETAILS.URL")}
              </span>
              <CopyToClipboardButton textToCopy={magicUrl.uri} isHoverable>
                <div className="min-w-0 truncate text-sm font-medium">
                  {magicUrl.uri}
                </div>
              </CopyToClipboardButton>
            </div>
          </div>
        </CardContent>
      </Card>
      <Dialog
        open={isDeactivateModalOpen}
        onOpenChange={setIsDeactivateModalOpen}
      >
        <ConfirmationModal
          onCancel={() => setIsDeactivateModalOpen(false)}
          onConfirm={confirmDeactivate}
          data={{
            dialogTitle: t("MAGIC_URL_DETAILS.DEACTIVATE_MAGIC_URL"),
            dialogSubtitle:
              t("MAGIC_URL_DETAILS.ARE_YOU_SURE_YOU_WANT"),
            confirmButton: t("MAGIC_URL_DETAILS.DEACTIVATE"),
            cancelButton: t("common:CANCEL"),
          }}
          buttonState={{ confirm: { disable: isRemoving } }}
        />
      </Dialog>
    </div>
  );
}
