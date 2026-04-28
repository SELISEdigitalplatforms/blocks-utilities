
import { Badge } from "@/components/ui-kits/badge/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { MaskedText } from "@/components/masked-text";
import { ReactNode, useState } from "react";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { format } from "date-fns";
import { IClientCredentialsConfig } from "@blocks-idp/authentication/models/auth.oidc.model";
import { Button } from "@/components/ui-kits/button/button";
import { useDeleteAuthClient } from "@blocks-idp/authentication/hooks/use-auth-clients";
import { useProjectStore } from "@/store/useProjectStore";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { isErrorWithErrors } from "@/lib/error";

const Item = ({ label, children }: { label: string; children: ReactNode }) => {
  return (
    <div className="min-w-0">
      <p className="mb-2 text-sm font-medium text-low-emphasis">{label}</p>
      <div className="break-words text-base font-normal text-high-emphasis">{children}</div>
    </div>
  );
};

type ClientInfoCardProps = {
  clientCredential: IClientCredentialsConfig;
};

export const ClientCredentialsCard = ({ clientCredential }: ClientInfoCardProps) => {
  const [open, setOpen] = useState<boolean>(false);
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { mutateAsync, isPending } = useDeleteAuthClient({
    projectKey: tenantId,
  });

  const handleConfirmDelete = async (id: string) => {
    try {
      const payload = {
        itemId: id,
        projectKey: tenantId,
      };
      const res = await mutateAsync(payload);
      if (!res.isSuccess) return showErrorToast({ errors: res.error });
      showSuccessToast({ description: "Client credential deleted successfully" });
      setOpen(false);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Something went wrong" });
    }
  };

  return (
    <div className="grid gap-4">
      <Card className="py-6" key={clientCredential.itemId}>
        <CardHeader>
          <div className="flex items-center justify-between gap-4">
            <div className="flex gap-3">
              <CardTitle> {clientCredential.name} </CardTitle>
              {clientCredential.isActive && <Badge variant="success">Active</Badge>}
            </div>
            <div>
              <Button
                onClick={() => {
                  setOpen(true);
                }}
                variant="outline"
                className="text-[#D92127]"
              >
                Delete
              </Button>
            </div>
          </div>
        </CardHeader>

        <CardContent>
          <div className="flex flex-col gap-8">
            <div className="grid grid-cols-1 gap-6 md:grid-cols-2 lg:grid-cols-3">
              <Item label="Client Id">
                <CopyToClipboardButton textToCopy={clientCredential.itemId}>
                  <MaskedText
                    text={clientCredential.itemId}
                    length={30}
                    showFirstN={4}
                    showLastN={4}
                  />
                </CopyToClipboardButton>
              </Item>

              <Item label="Client Secret">
                <CopyToClipboardButton textToCopy={clientCredential.clientSecret}>
                  <MaskedText
                    text={clientCredential.clientSecret}
                    length={30}
                    showFirstN={4}
                    showLastN={4}
                  />
                </CopyToClipboardButton>
              </Item>

              <Item label="Audience">
                <div className="flex items-center gap-2">
                  {clientCredential.roles &&
                  clientCredential.audiences &&
                  clientCredential.audiences.length > 0 ? (
                    <div className="flex flex-wrap gap-1.5">
                      {clientCredential.audiences.map((audience: string, index) => (
                        <span key={index}>{audience}</span>
                      ))}
                    </div>
                  ) : (
                    <span>N/A</span>
                  )}
                </div>
              </Item>

              <Item label="Role(s)">
                <div className="flex items-center gap-2">
                  {clientCredential.roles && clientCredential.roles.length > 0 ? (
                    <div className="flex flex-wrap gap-1.5">
                      {clientCredential.roles.map((role: string, index) => (
                        <Badge key={index} variant="secondary" className="text-xs">
                          {role}
                        </Badge>
                      ))}
                    </div>
                  ) : (
                    <span>N/A</span>
                  )}
                </div>
              </Item>

              <Item label="Created on">
                <span className="whitespace-nowrap">
                  {format(clientCredential.createdDate, "dd/MM/yyyy HH:mm")}
                </span>
              </Item>
            </div>
          </div>
        </CardContent>
      </Card>

      <Dialog open={open} onOpenChange={setOpen}>
        <ConfirmationModal
          onCancel={() => setOpen(false)}
          onConfirm={() => handleConfirmDelete(clientCredential.itemId)}
          data={{
            dialogTitle: "Delete",
            dialogSubtitle: `Are you sure you want to delete client-credential`,
          }}
          buttonState={{
            confirm: { disable: isPending },
          }}
        />
      </Dialog>
    </div>
  );
};
