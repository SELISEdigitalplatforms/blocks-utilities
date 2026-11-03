
import React, { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { useToggleCaptchaConfigStatus } from "../hooks/use-captcha-config";
import { useProjectStore, useT } from "@seliseblocks/genesis-os";

import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { getCaptchaProviders, ICaptchaConfig } from "../models/captcha";
import { isErrorWithErrors } from "@/lib/error";
import { Check, X } from "lucide-react";

type ToggleCaptchaStatusModalProps = {
  configuration: ICaptchaConfig;
};

export const ToggleCaptchaStatusModal = ({ configuration }: ToggleCaptchaStatusModalProps) => {
  const { t } = useT("utilities.idp");
  const [open, setOpen] = useState<boolean>(false);
  const { isPending, mutateAsync } = useToggleCaptchaConfigStatus();
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  const providerType = getCaptchaProviders()[configuration.provider];
  const onConfirm = async () => {
    try {
      if (!configuration) return showErrorToast({ errors: t("TOGGLE_CAPTCHA_STATUS_MODAL.SOMETHING_WENT_WRONG") });

      const res = await mutateAsync({
        projectKey: tenantId,
        isEnable: !configuration.isEnable,
        itemId: configuration.itemId,
      });

      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({
        description: (configuration.isEnable ? t("TOGGLE_CAPTCHA_STATUS_MODAL.IS_DISABLED_SUCCESSFULLY", { label: providerType.label }) : t("TOGGLE_CAPTCHA_STATUS_MODAL.IS_ENABLED_SUCCESSFULLY", { label: providerType.label })),
      });
      setOpen(false);
    } catch (error) {
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors });
      }
    }
  };

  const IconComponent = configuration?.isEnable ? X : Check;

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger>
        <Button size="sm" variant="outline">
          <IconComponent className="h-4 w-4" />
          <span className="ml-2.5">{configuration?.isEnable ? t("TOGGLE_CAPTCHA_STATUS_MODAL.DISABLE") : t("TOGGLE_CAPTCHA_STATUS_MODAL.ENABLE")}</span>
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{(configuration?.isEnable ? t("TOGGLE_CAPTCHA_STATUS_MODAL.DISABLE_CAPTCHA") : t("TOGGLE_CAPTCHA_STATUS_MODAL.ENABLE_CAPTCHA"))}</DialogTitle>
          <DialogDescription>
            {(configuration?.isEnable ? t("TOGGLE_CAPTCHA_STATUS_MODAL.ARE_YOU_SURE_DISABLE", { label: providerType.label }) : t("TOGGLE_CAPTCHA_STATUS_MODAL.ARE_YOU_SURE_ENABLE", { label: providerType.label }))}
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <DialogTrigger asChild>
            <Button variant="outline" size="sm">
              {t("common:CANCEL")}
            </Button>
          </DialogTrigger>
          <Button size="sm" onClick={onConfirm} disabled={isPending}>
            {t("common:YES")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
