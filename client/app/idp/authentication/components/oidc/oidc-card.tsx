
import { Badge } from "@/components/ui-kits/badge/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { MaskedText } from "@/components/masked-text";
import { ReactNode, useState } from "react";
import { getApiUrl } from "@/lib/get-api-path";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { format } from "date-fns";
import {
  IDeleteOidcClientPayload,
  IOidcConfig,
} from "@blocks-idp/authentication/models/auth.oidc.model";
import { Button } from "@/components/ui-kits/button/button";
import { useProjectStore, useT } from "@seliseblocks/genesis-os";

import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { useDeleteAuthOidc } from "@blocks-idp/authentication/hooks/use-auth-oidc";
import { isErrorWithErrors } from "@/lib/error";
import { Trash } from "lucide-react";
import { CreateOIDC } from "../create-oidc/create-oidc";

const Item = ({ label, children }: { label: string; children: ReactNode }) => {
  return (
    <div className="min-w-0">
      <p className="mb-2 text-sm font-medium text-low-emphasis">{label}</p>
      <div className="break-words text-base font-normal text-high-emphasis">{children}</div>
    </div>
  );
};

type OIDCCardProps = {
  oidc: IOidcConfig;
};

export const OIDCCard = ({ oidc }: OIDCCardProps) => {
  const { t } = useT("utilities.idp.authentication");
  const [open, setOpen] = useState<boolean>(false);
  const tenantId = useProjectStore().selectedProject?.tenantId || "";

  const { mutateAsync, isPending } = useDeleteAuthOidc({
    projectKey: tenantId,
  });

  const handleConfirmDelete = async (id: string) => {
    try {
      const payload: IDeleteOidcClientPayload = {
        itemId: id,
        projectKey: tenantId,
      };

      const res = await mutateAsync(payload);
      if (!res.isSuccess) return showErrorToast({ errors: res.error });
      showSuccessToast({ description: t("OIDC_CARD.OIDC_CREDENTIAL_DELETED") });
      setOpen(false);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: t("OIDC_CARD.SOMETHING_WENT_WRONG") });
    }
  };

  return (
    <div className="grid gap-4">
      <Card className="py-6">
        <CardHeader>
          <div className="flex items-center justify-between gap-4">
            <div className="flex items-center gap-4">
              {oidc.clientLogoUrl && (
                <div className="relative h-12 w-12 overflow-hidden rounded-lg">
                  <img src={oidc.clientLogoUrl} alt={t("OIDC_CARD.OIDC_LOGO")} className="object-cover" />
                </div>
              )}
              <CardTitle>{oidc.clientDisplayName}</CardTitle>
            </div>
            <div className="flex">
              <div className="mt-0.5">
                <CreateOIDC itemId={oidc.itemId} triggerVariant="ghost" />
              </div>
              <Button onClick={() => setOpen(true)} variant="ghost" className="hover:text-error">
                <Trash size={16} />
              </Button>
            </div>
          </div>
        </CardHeader>

        <CardContent>
          <div className="flex flex-col gap-8">
            <div className="grid grid-cols-1 gap-6 md:grid-cols-2 lg:grid-cols-3">
              <Item label={t("OIDC_CARD.CLIENT_ID")}>
                <CopyToClipboardButton textToCopy={oidc.itemId}>
                  <MaskedText text={oidc.itemId} length={30} showFirstN={4} showLastN={4} />
                </CopyToClipboardButton>
              </Item>

              <Item label={t("OIDC_CARD.CLIENT_SECRET")}>
                <CopyToClipboardButton textToCopy={oidc.clientSecret}>
                  <MaskedText text={oidc.clientSecret} length={30} showFirstN={4} showLastN={4} />
                </CopyToClipboardButton>
              </Item>

              <Item label={t("OIDC_CARD.REDIRECT_URL")}>
                <CopyToClipboardButton textToCopy={oidc.redirectUri}>
                  {oidc.redirectUri}
                </CopyToClipboardButton>
              </Item>

              <Item label={t("OIDC_CARD.AUDIENCE")}>
                <CopyToClipboardButton textToCopy={oidc.audience}>
                  <div className="flex items-center gap-2">
                    <div className="flex flex-wrap gap-1.5">{oidc.audience}</div>
                  </div>
                </CopyToClipboardButton>
              </Item>

              <Item label={t("OIDC_CARD.SCOPE_S")}>
                <div className="flex items-center gap-2">
                  <div className="flex flex-wrap gap-1.5">
                    {oidc.scope ? (
                      <Badge variant="secondary" className="text-xs">
                        {oidc.scope}
                      </Badge>
                    ) : (
                      <span>N/A</span>
                    )}
                  </div>
                </div>
              </Item>

              <Item label={t("OIDC_CARD.CREATED_ON")}>
                <span className="whitespace-nowrap">
                  {format(oidc.createdDate, "dd/MM/yyyy HH:mm")}
                </span>
              </Item>

              <Item label={t("OIDC_CARD.THEME_COLOR")}>
                <div className="flex items-center gap-3">
                  {oidc.clientBrandColor && (
                    <div
                      className="h-8 w-8 rounded-lg border border-border"
                      style={{ backgroundColor: oidc.clientBrandColor }}
                      title={oidc.clientBrandColor}
                    />
                  )}
                  <span className="font-mono">{oidc.clientBrandColor || "N/A"}</span>
                </div>
              </Item>

              <div className="md:col-span-2">
                <Item label={t("OIDC_CARD.WELL_KNOWN_URL")}>
                  <CopyToClipboardButton
                    textToCopy={`${getApiUrl("idp/v1", ".well-known/openid-configuration")}?projectKey=${tenantId}`}
                  >
                    <span className="break-all">
                      {`${getApiUrl("idp/v1", ".well-known/openid-configuration")}?projectKey=${tenantId}`}
                    </span>
                  </CopyToClipboardButton>
                </Item>
              </div>
            </div>
          </div>
        </CardContent>
      </Card>

      <Dialog open={open} onOpenChange={setOpen}>
        <ConfirmationModal
          onCancel={() => setOpen(false)}
          onConfirm={() => handleConfirmDelete(oidc.itemId)}
          data={{
            dialogTitle: t("common:DELETE"),
            dialogSubtitle: t("OIDC_CARD.ARE_YOU_SURE_YOU_WANT"),
          }}
          buttonState={{
            confirm: { disable: isPending },
          }}
        />
      </Dialog>
    </div>
  );
};
