import React, { useState, useEffect } from "react";
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogFooter,
  DialogContent,
} from "@/components/ui-kits/dialog/dialog";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { ISaveMagicUrlConfigPayload } from "@blocks-utilities/models/magic-url-config.model";
import { useGetMagicUrlConfig } from "@blocks-utilities/hooks/use-magic-url";
import { useQueryClient } from "@tanstack/react-query";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { getDefaultShortUrlBase, isValidUrl } from "@blocks-utilities/utils/url.util";
import { Loader2 } from "lucide-react";

interface MagicUrlConfigDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  trigger?: React.ReactNode;
  onSave?: (config: ISaveMagicUrlConfigPayload) => Promise<void>;
  projectKey?: string;
}

export const MagicUrlConfigDialog = ({
  open,
  onOpenChange,
  trigger,
  onSave,
  projectKey,
}: MagicUrlConfigDialogProps) => {
  const [contextName, setContextName] = useState("");
  const [shortUrlBase, setShortUrlBase] = useState("");
  const [isLoading, setIsLoading] = useState(false);
  const [errors, setErrors] = useState({ contextName: "", shortUrlBase: "" });

  const queryClient = useQueryClient();
  const { data: configData, isLoading: isConfigLoading } = useGetMagicUrlConfig(projectKey || "", {
    enabled: open && !!projectKey,
  });

  useEffect(() => {
    if (open && configData) {
      if (configData.config) {
        setContextName(configData.config.contextName || "");
        setShortUrlBase(configData.config.shortUrlBase || "");
      } else {
        setContextName("Default");
        setShortUrlBase(getDefaultShortUrlBase());
      }
      setErrors({ contextName: "", shortUrlBase: "" });
    }
  }, [open, configData]);

  const validateFields = (): boolean => {
    const newErrors = { contextName: "", shortUrlBase: "" };

    if (!contextName.trim()) {
      newErrors.contextName = "Context name is required";
    }

    if (!shortUrlBase.trim()) {
      newErrors.shortUrlBase = "Short URL base is required";
    } else if (!isValidUrl(shortUrlBase)) {
      newErrors.shortUrlBase = "URL must be a valid HTTPS/HTTP URL";
    } else if (!shortUrlBase.endsWith("/")) {
      newErrors.shortUrlBase = "URL must end with a forward slash (/)";
    }

    setErrors(newErrors);
    return !newErrors.contextName && !newErrors.shortUrlBase;
  };

  const handleSave = async () => {
    if (!projectKey) return;
    if (!validateFields()) return;

    setIsLoading(true);
    try {
      const payload: ISaveMagicUrlConfigPayload = { contextName, shortUrlBase, projectKey };

      if (onSave) {
        await onSave(payload);
      }

      await queryClient.invalidateQueries({ queryKey: ["magic-url-config", projectKey] });

      if (!configData?.isSuccess) return showErrorToast({ errors: configData?.errorMessage });
      showSuccessToast({ description: "Configuration updated successfully" });

      onOpenChange(false);
    } catch (error) {
      console.error("Failed to save config:", error);
    } finally {
      setIsLoading(false);
    }
  };

  const handleOpenChange = (newOpen: boolean) => {
    onOpenChange(newOpen);
  };

  return (
    <>
      {trigger && (
        <div onClick={() => handleOpenChange(true)} className="cursor-pointer">
          {trigger}
        </div>
      )}
      <Dialog open={open} onOpenChange={handleOpenChange}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Configure Magic URL</DialogTitle>
          </DialogHeader>

          {isConfigLoading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="h-4 w-4 animate-spin" />
            </div>
          ) : (
            <>
              <div className="space-y-4 py-4">
                <div className="space-y-2">
                  <Label htmlFor="contextName">
                    Context Name <span className="text-error">*</span>
                  </Label>
                  <Input
                    id="contextName"
                    placeholder="Enter context name"
                    value={contextName}
                    onChange={(e) => {
                      setContextName(e.target.value);
                      if (errors.contextName) setErrors((prev) => ({ ...prev, contextName: "" }));
                    }}
                    disabled={isLoading}
                    className={errors.contextName ? "border-error" : ""}
                  />
                  {errors.contextName && <p className="text-sm text-error">{errors.contextName}</p>}
                </div>

                <div className="space-y-2">
                  <Label htmlFor="shortUrlBase">
                    Short URL Base <span className="text-error">*</span>
                  </Label>
                  <Input
                    id="shortUrlBase"
                    placeholder="e.g., https://short.seliseblocks.com/"
                    value={shortUrlBase}
                    onChange={(e) => {
                      setShortUrlBase(e.target.value);
                      if (errors.shortUrlBase) setErrors((prev) => ({ ...prev, shortUrlBase: "" }));
                    }}
                    disabled={isLoading}
                    className={errors.shortUrlBase ? "border-error" : ""}
                  />
                  {errors.shortUrlBase && (
                    <p className="text-sm text-error">{errors.shortUrlBase}</p>
                  )}
                </div>
              </div>

              <DialogFooter>
                <Button
                  variant="outline"
                  onClick={() => handleOpenChange(false)}
                  disabled={isLoading}
                >
                  Cancel
                </Button>
                <Button onClick={handleSave} disabled={isLoading}>
                  {isLoading ? "Saving..." : "Save"}
                </Button>
              </DialogFooter>
            </>
          )}
        </DialogContent>
      </Dialog>
    </>
  );
};
