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
import { Switch } from "@/components/ui-kits/switch/switch";
import { Calendar } from "@/components/ui-kits/calendar/calendar";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { CalendarIcon } from "lucide-react";
import { formatDate } from "@/lib/utils";
import { MagicUrl } from "@blocks-utilities/models/magic-url.model";
import { useCreateMagicUrl } from "@blocks-utilities/hooks/use-magic-url";
import { useProjectStore } from "@/store/useProjectStore";
import { toast } from "@/hooks/use-toast";
import { useAuthStore } from "@/store/useAuthStore";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { ScrollArea } from "@/components/ui-kits/scroll-area/scroll-area";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { magicUrlSchema } from "@blocks-utilities/utils/url.util";

type MagicUrlFormData = z.infer<typeof magicUrlSchema>;

interface MagicUrlDialogProps {
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  trigger?: React.ReactNode;
  initialData?: MagicUrl;
}

export function MagicUrlDialog({ open, onOpenChange, trigger, initialData }: MagicUrlDialogProps) {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  const { user } = useAuthStore();
  const userId = user?.itemId || "";

  const {
    register,
    formState: { errors, isValid },
    reset,
    setValue,
  } = useForm<MagicUrlFormData>({
    resolver: zodResolver(magicUrlSchema),
    mode: "onChange",
    defaultValues: { uri: "", name: "" },
  });

  const [url, setUrl] = useState("");
  const [name, setName] = useState("");
  const [type, setType] = useState<string>("1");
  const [requestMethod, setRequestMethod] = useState<string>("GET");
  const [requestPayload, setRequestPayload] = useState("");
  const [requestHeaders, setRequestHeaders] = useState("");
  const [requestEncodedQueryString, setRequestEncodedQueryString] = useState("");
  const [clientCredential, setClientCredential] = useState("");
  const [linkBasedActionConfigId, setLinkBasedActionConfigId] = useState("");
  const [cache, setCache] = useState(false);
  const [userCanLogin, setUserCanLogin] = useState(false);
  const [usageLimit, setUsageLimit] = useState(false);
  const [usageLimitValue, setUsageLimitValue] = useState("");
  const [autoExpiry, setAutoExpiry] = useState(false);
  const [expiryDate, setExpiryDate] = useState<Date>();
  const [isCalendarOpen, setIsCalendarOpen] = useState(false);

  const { mutate: createMagicUrl, isPending } = useCreateMagicUrl();

  const resetForm = React.useCallback(() => {
    setUrl("");
    setName("");
    setType("1");
    setRequestMethod("GET");
    setRequestPayload("");
    setRequestHeaders("");
    setRequestEncodedQueryString("");
    setClientCredential("");
    setLinkBasedActionConfigId("");
    setCache(false);
    setUserCanLogin(false);
    setUsageLimit(false);
    setUsageLimitValue("");
    setAutoExpiry(false);
    setExpiryDate(undefined);
    setIsCalendarOpen(false);
    reset({ uri: "", name: "" });
  }, [reset]);

  React.useEffect(() => {
    if (open) {
      if (initialData) {
        setUrl(initialData.uri);
        setName(initialData.name || "");
        setValue("uri", initialData.uri, { shouldValidate: true });
        setValue("name", initialData.name || "", { shouldValidate: true });
        setType(initialData.type || "1");
        setRequestMethod(initialData.requestMethod || "GET");
        setClientCredential(initialData.clientCredential || "");
        setLinkBasedActionConfigId(initialData.linkBasedActionConfigId || "");
        setCache(initialData.persistent || false);
        setUsageLimit(initialData.usageLimit > 0);
        setUsageLimitValue(initialData.usageLimit > 0 ? initialData.usageLimit.toString() : "");
        setAutoExpiry(!!initialData.expiryLifeSpan || !!initialData.expiryDate);
        setExpiryDate(initialData.expiryDate ? new Date(initialData.expiryDate) : undefined);
      } else {
        resetForm();
      }
    }
  }, [open, initialData, setValue, resetForm]);

  const handleShorten = () => {
    if (!isValid) {
      toast({
        variant: "destructive",
        title: "Validation Error",
        description: "Please fix the errors in the form",
      });
      return;
    }

    let expiryLifeSpan: number | undefined;
    if (autoExpiry && expiryDate) {
      const now = new Date();
      const diffMs = expiryDate.getTime() - now.getTime();
      expiryLifeSpan = diffMs * 10000;
    }

    const payload = {
      uri: url,
      name,
      type: parseInt(type),
      ...(type !== "1" && { requestMethod }),
      requestPayload,
      requestHeaders,
      requestEncodedQueryString,
      clientCredential,
      linkBasedActionConfigId,
      persistent: cache,
      userCanLogin,
      projectKey: tenantId,
      usageLimit: usageLimit ? parseInt(usageLimitValue) || 0 : 0,
      expiryLifeSpan,
      requestByUserId: userId,
    };

    createMagicUrl(payload, {
      onSuccess: () => {
        toast({ variant: "success", title: "Success", description: "Magic URL created successfully" });
        handleCancel();
      },
      onError: (error) => {
        toast({
          variant: "destructive",
          title: "Error",
          description: error instanceof Error ? error.message : "Failed to create Magic URL",
        });
      },
    });
  };

  const handleCancel = () => {
    resetForm();
    onOpenChange?.(false);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {trigger && <DialogTrigger asChild>{trigger}</DialogTrigger>}
      <DialogContent className="max-w-3xl">
        <DialogHeader>
          <DialogTitle>Magic URL</DialogTitle>
          <DialogDescription>Create a new Magic URL with custom configurations.</DialogDescription>
        </DialogHeader>

        <ScrollArea className="max-h-[60vh] pr-6">
          <div className="grid gap-6 px-1 py-4">
            <div className="grid grid-cols-2 gap-4">
              <div className="grid gap-2">
                <Label htmlFor="url">URI *</Label>
                <Input
                  id="url"
                  placeholder="https://example.com"
                  {...register("uri")}
                  value={url}
                  onChange={(e) => {
                    setUrl(e.target.value);
                    setValue("uri", e.target.value, { shouldValidate: true });
                  }}
                />
                <div className="min-h-[1.25rem]">
                  {errors.uri && <p className="text-xs text-error">{errors.uri.message}</p>}
                </div>
              </div>
              <div className="grid gap-2">
                <Label htmlFor="name">Name *</Label>
                <Input
                  id="name"
                  placeholder="My Magic Link"
                  {...register("name")}
                  value={name}
                  onChange={(e) => {
                    setName(e.target.value);
                    setValue("name", e.target.value, { shouldValidate: true });
                  }}
                />
                <div className="min-h-[1.25rem]">
                  {errors.name && <p className="text-xs text-error">{errors.name.message}</p>}
                </div>
              </div>
            </div>

            <div className={type === "1" ? "grid gap-2" : "grid grid-cols-2 gap-4"}>
              <div className="grid gap-2">
                <Label>Type</Label>
                <Select value={type} onValueChange={setType}>
                  <SelectTrigger>
                    <SelectValue placeholder="Select type" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="1">Redirect</SelectItem>
                    <SelectItem value="0">Action</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              {type !== "1" && (
                <div className="grid gap-2">
                  <Label>Request Method</Label>
                  <Select value={requestMethod} onValueChange={setRequestMethod}>
                    <SelectTrigger>
                      <SelectValue placeholder="Select method" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="GET">GET</SelectItem>
                      <SelectItem value="POST">POST</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
              )}
            </div>

            {type !== "1" && (
              <>
                <div className="grid gap-2">
                  <Label htmlFor="payload">Request Payload</Label>
                  <Textarea
                    id="payload"
                    placeholder="{}"
                    value={requestPayload}
                    onChange={(e) => setRequestPayload(e.target.value)}
                    className="h-20"
                  />
                </div>
                <div className="grid gap-2">
                  <Label htmlFor="headers">Request Headers</Label>
                  <Textarea
                    id="headers"
                    placeholder='{"Authorization": "Bearer..."}'
                    value={requestHeaders}
                    onChange={(e) => setRequestHeaders(e.target.value)}
                    className="h-20"
                  />
                </div>
              </>
            )}

            <div className={type === "1" ? "grid gap-2" : "grid grid-cols-2 gap-4"}>
              {type !== "1" && (
                <div className="grid gap-2">
                  <Label htmlFor="encodedQuery">Encoded Query String</Label>
                  <Input
                    id="encodedQuery"
                    value={requestEncodedQueryString}
                    onChange={(e) => setRequestEncodedQueryString(e.target.value)}
                  />
                </div>
              )}
              <div className="grid gap-2">
                <Label htmlFor="clientCred">Client Credential</Label>
                <Input
                  id="clientCred"
                  type="password"
                  value={clientCredential}
                  onChange={(e) => setClientCredential(e.target.value)}
                />
              </div>
            </div>

            <div className="grid gap-4 rounded-md border p-4">
              <div className="grid gap-3">
                <div className="flex items-center justify-between">
                  <Label htmlFor="usage-limit" className="font-medium">
                    Set Usage Limit
                  </Label>
                  <Switch id="usage-limit" checked={usageLimit} onCheckedChange={setUsageLimit} />
                </div>
                {usageLimit && (
                  <Input
                    type="number"
                    placeholder="Enter usage limit"
                    value={usageLimitValue}
                    onChange={(e) => setUsageLimitValue(e.target.value)}
                    min="1"
                  />
                )}
              </div>

              <div className="grid gap-3">
                <div className="flex items-center justify-between">
                  <Label htmlFor="auto-expiry" className="font-medium">
                    Set Auto Expiry Date
                  </Label>
                  <Switch id="auto-expiry" checked={autoExpiry} onCheckedChange={setAutoExpiry} />
                </div>
                {autoExpiry && (
                  <Popover open={isCalendarOpen} onOpenChange={setIsCalendarOpen}>
                    <PopoverTrigger asChild>
                      <Button
                        variant="outline"
                        className="justify-start text-left font-normal"
                        type="button"
                        onClick={() => setIsCalendarOpen(true)}
                      >
                        <CalendarIcon className="mr-2 h-4 w-4" />
                        {expiryDate ? formatDate(expiryDate, true) : "Pick a date"}
                      </Button>
                    </PopoverTrigger>
                    <PopoverContent className="w-auto p-0" align="start">
                      <Calendar
                        mode="single"
                        selected={expiryDate}
                        onSelect={(date) => {
                          setExpiryDate(date);
                          setIsCalendarOpen(false);
                        }}
                        disabled={(date) => {
                          const today = new Date();
                          today.setHours(0, 0, 0, 0);
                          const selectedDate = new Date(date);
                          selectedDate.setHours(0, 0, 0, 0);
                          return selectedDate < today;
                        }}
                      />
                    </PopoverContent>
                  </Popover>
                )}
              </div>
            </div>
          </div>
        </ScrollArea>

        <DialogFooter className="mt-4">
          <Button variant="outline" onClick={handleCancel} disabled={isPending}>
            Cancel
          </Button>
          <Button onClick={handleShorten} disabled={isPending || !isValid}>
            {isPending ? "Creating..." : "Create"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
