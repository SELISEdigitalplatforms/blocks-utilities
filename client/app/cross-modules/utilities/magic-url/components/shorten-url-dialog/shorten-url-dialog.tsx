import React, { useState } from "react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { RadioGroup, RadioGroupItem } from "@/components/ui-kits/radio-group/radio-group";
import { Switch } from "@/components/ui-kits/switch/switch";
import { Calendar } from "@/components/ui-kits/calendar/calendar";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { CalendarIcon, Check } from "lucide-react";
import { formatDate } from "@/lib/utils";
import { useT } from "@seliseblocks/genesis-os";

interface ShortenUrlDialogProps {
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  trigger?: React.ReactNode;
}

export function ShortenUrlDialog({ open, onOpenChange, trigger }: ShortenUrlDialogProps) {
  const { t } = useT("utilities.magic-url");
  const [url, setUrl] = useState("");
  const [urlType, setUrlType] = useState("auto");
  const [alias, setAlias] = useState("");
  const [cache, setCache] = useState(false);
  const [usageLimit, setUsageLimit] = useState(false);
  const [usageLimitValue, setUsageLimitValue] = useState("");
  const [autoExpiry, setAutoExpiry] = useState(false);
  const [expiryDate, setExpiryDate] = useState<Date>();

  const isAliasValid = alias.length >= 5;

  const handleShorten = () => {
    // TODO: Implement shorten logic
    console.error({
      url,
      urlType,
      alias: urlType === "alias" ? alias : undefined,
      cache,
      usageLimit: usageLimit ? usageLimitValue : undefined,
      expiryDate: autoExpiry ? expiryDate : undefined,
    });
  };

  const handleCancel = () => {
    // Reset form
    setUrl("");
    setUrlType("auto");
    setAlias("");
    setCache(false);
    setUsageLimit(false);
    setUsageLimitValue("");
    setAutoExpiry(false);
    setExpiryDate(undefined);
    onOpenChange?.(false);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {trigger && <DialogTrigger asChild>{trigger}</DialogTrigger>}
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>{t("SHORTEN_URL_DIALOG.SHORT_URL")}</DialogTitle>
          <DialogDescription>{t("SHORTEN_URL_DIALOG.ENTER_AN_URL_YOU_WANT")}</DialogDescription>
        </DialogHeader>

        <div className="grid gap-6 py-4">
          {/* Enter URL */}
          <div className="grid gap-2">
            <Label htmlFor="url">{t("SHORTEN_URL_DIALOG.ENTER_URL")}</Label>
            <Input
              id="url"
              type="url"
              placeholder="https://example.com"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
            />
          </div>

          {/* Shortened URL Type */}
          <div className="grid gap-3">
            <Label>{t("SHORTEN_URL_DIALOG.SHORTENED_URL_TYPE")}</Label>
            <RadioGroup value={urlType} onValueChange={setUrlType}>
              <div className="flex items-center space-x-2">
                <RadioGroupItem value="auto" id="auto" />
                <Label htmlFor="auto" className="cursor-pointer font-normal">
                  {t("SHORTEN_URL_DIALOG.AUTO_GENERATED")}
                </Label>
              </div>
              <div className="flex items-center space-x-2">
                <RadioGroupItem value="alias" id="alias" />
                <Label htmlFor="alias" className="cursor-pointer font-normal">
                  {t("SHORTEN_URL_DIALOG.SET_ALIAS")}
                </Label>
              </div>
            </RadioGroup>

            {/* Alias Input */}
            {urlType === "alias" && (
              <div className="grid gap-2 pl-6">
                <Input
                  type="text"
                  placeholder={t("SHORTEN_URL_DIALOG.ENTER_ALIAS")}
                  value={alias}
                  onChange={(e) => setAlias(e.target.value)}
                />
                {alias && (
                  <div className="flex items-center gap-2 text-sm">
                    <span className="text-medium-emphasis">{t("SHORTEN_URL_DIALOG.MIN_5_CHARACTERS")}</span>
                    {isAliasValid && (
                      <>
                        <Check className="h-4 w-4 text-success" />
                        <span className="text-success">{t("SHORTEN_URL_DIALOG.ALIAS_IS_AVAILABLE")}</span>
                      </>
                    )}
                  </div>
                )}
              </div>
            )}
          </div>

          {/* Cache Switch */}
          <div className="flex items-center justify-between">
            <Label htmlFor="cache" className="font-normal">
              {t("SHORTEN_URL_DIALOG.CACHE_FOR_FASTER_RESOLUTION")}
            </Label>
            <Switch id="cache" checked={cache} onCheckedChange={setCache} />
          </div>

          {/* Usage Limit Switch */}
          <div className="grid gap-3">
            <div className="flex items-center justify-between">
              <Label htmlFor="usage-limit" className="font-normal">
                {t("SHORTEN_URL_DIALOG.SET_USAGE_LIMIT")}
              </Label>
              <Switch id="usage-limit" checked={usageLimit} onCheckedChange={setUsageLimit} />
            </div>
            {usageLimit && (
              <Input
                type="number"
                placeholder={t("SHORTEN_URL_DIALOG.ENTER_USAGE_LIMIT")}
                value={usageLimitValue}
                onChange={(e) => setUsageLimitValue(e.target.value)}
                min="1"
              />
            )}
          </div>

          {/* Auto Expiry Date Switch */}
          <div className="grid gap-3">
            <div className="flex items-center justify-between">
              <Label htmlFor="auto-expiry" className="font-normal">
                {t("SHORTEN_URL_DIALOG.SET_AUTO_EXPIRY_DATE")}
              </Label>
              <Switch id="auto-expiry" checked={autoExpiry} onCheckedChange={setAutoExpiry} />
            </div>
            {autoExpiry && (
              <Popover>
                <PopoverTrigger asChild>
                  <Button variant="outline" className="justify-start text-left font-normal">
                    <CalendarIcon className="mr-2 h-4 w-4" />
                    {expiryDate ? formatDate(expiryDate, true) : t("SHORTEN_URL_DIALOG.PICK_A_DATE")}
                  </Button>
                </PopoverTrigger>
                <PopoverContent className="w-auto p-0" align="start">
                  <Calendar
                    mode="single"
                    selected={expiryDate}
                    onSelect={setExpiryDate}
                    initialFocus
                    disabled={(date) => date < new Date()}
                  />
                </PopoverContent>
              </Popover>
            )}
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={handleCancel}>
            {t("common:CANCEL")}
          </Button>
          <Button onClick={handleShorten}>{t("SHORTEN_URL_DIALOG.SHORTEN")}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
