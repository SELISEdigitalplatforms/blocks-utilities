

import { Button } from "@/components/ui-kits/button/button";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { getCaptchaProviders, ICaptchaConfig } from "../../models/captcha";
import { ConfigureCaptchaModal } from "../../modals/configure-captcha-modal/";
import { DialogTrigger } from "@/components/ui-kits/dialog/dialog";
import { Pencil } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { MaskedText } from "@/components/masked-text";
import { ReactNode } from "react";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { ToggleCaptchaStatusModal } from "@blocks-idp/captcha/modals/toggle-captcha-status-modal";
import { useT } from "@seliseblocks/genesis-os";

const LoadingSkelton = () => {
  return (
    <div className="grid gap-2">
      {Array.from({ length: 4 }).map((_, index) => (
        <Skeleton key={index} className="h-12 w-full rounded" />
      ))}
    </div>
  );
};
const EmptyCaptchaConfig = () => {
  const { t } = useT("utilities.idp");
  return (
    <div className="text-muted- flex h-32 flex-wrap items-center justify-center rounded-sm border bg-background p-4 text-center text-muted-foreground">
      {t("CONFIGURE_CAPTCHA_LIST.NO_CONFIGURATIONS_FOUND_PLEASE")}
    </div>
  );
};
const Item = ({ label, children }: { label: string; children: ReactNode }) => {
  return (
    <div>
      <p className="text-sm font-medium text-low-emphasis">{label}</p>
      <div className="text-wrap text-base font-normal text-high-emphasis">{children}</div>
    </div>
  );
};

type ConfigureCaptchaListProps = {
  isLoading: boolean;
  configurations: ICaptchaConfig[];
};

export const ConfigureCaptchaList = ({ isLoading, configurations }: ConfigureCaptchaListProps) => {
  const { t } = useT("utilities.idp");
  if (isLoading) return <LoadingSkelton />;
  if (!configurations.length) return <EmptyCaptchaConfig />;

  return (
    <>
      <div className="grid gap-4">
        {configurations.map((configuration) => (
          <>
            {getCaptchaProviders()[configuration.provider] && (
              <Card key={configuration.itemId}>
                <CardHeader className="flex-row justify-between">
                  <div className="flex items-center gap-4">
                    <CardTitle> {getCaptchaProviders()[configuration.provider].label} </CardTitle>
                    {configuration.isEnable && <Badge variant="success">{t("CONFIGURE_CAPTCHA_LIST.ENABLE")}</Badge>}
                  </div>
                  <div className="hidden gap-4 sm:flex">
                    <ConfigureCaptchaModal configuration={configuration}>
                      <DialogTrigger asChild>
                        <Button size="sm" variant="outline">
                          <Pencil className="h-4 w-4" />
                          <span className="ml-2.5">{t("common:EDIT")}</span>
                        </Button>
                      </DialogTrigger>
                    </ConfigureCaptchaModal>
                    <ToggleCaptchaStatusModal configuration={configuration} />
                  </div>
                </CardHeader>

                <CardContent>
                  <div className="flex flex-col gap-4">
                    <div className="flex gap-4 sm:hidden">
                      <ConfigureCaptchaModal configuration={configuration}>
                        <DialogTrigger asChild>
                          <Button size="sm" variant="outline">
                            <Pencil className="h-4 w-4" />
                            <span className="ml-2.5">{t("common:EDIT")}</span>
                          </Button>
                        </DialogTrigger>
                      </ConfigureCaptchaModal>
                      <ToggleCaptchaStatusModal configuration={configuration} />
                    </div>

                    <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                      <Item label={t("CONFIGURE_CAPTCHA_LIST.SITE_KEY")}>
                        <CopyToClipboardButton textToCopy={configuration.captchaKey}>
                          <MaskedText text={configuration.captchaSecret} length={30} />
                        </CopyToClipboardButton>
                      </Item>
                      <Item label={t("CONFIGURE_CAPTCHA_LIST.SECRET_KEY")}>
                        <CopyToClipboardButton textToCopy={configuration.captchaSecret}>
                          <MaskedText text={configuration.captchaSecret} length={30} />
                        </CopyToClipboardButton>
                      </Item>
                    </div>
                  </div>
                </CardContent>
              </Card>
            )}
          </>
        ))}
      </div>
    </>
  );
};
