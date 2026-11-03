

import { Button } from "@/components/ui-kits/button/button";
import {
  Drawer,
  DrawerClose,
  DrawerContent,
  DrawerTitle,
} from "@/components/ui-kits/drawer/drawer";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { cn } from "@/lib/utils";
import { useProjectStore, useT, tStatic } from "@seliseblocks/genesis-os";

import { useAddJwtClaim, useGetJwtClaim } from "@blocks-idp/authentication/hooks/use-jwt-claim";
import { JwtClaimPayload } from "@blocks-idp/authentication/models/jwt.claim.model";
import { jwtDecode } from "jwt-decode";
import { X } from "lucide-react";
import { useState, useCallback, useMemo } from "react";

const UTILITIES_IDP_AUTHENTICATION_MODULE = "utilities.idp.authentication";

interface DecodedJwt {
  [key: string]: unknown;
}

interface MapJwtClaimModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

type RequiredProperty = "userId" | "email" | "name" | "userName" | "roles";

const REQUIRED_PROPERTIES: RequiredProperty[] = ["userId", "email", "name", "userName", "roles"];

const getPropertyLabels = (): Record<RequiredProperty, string> => ({
  userId: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "MAP_JWT_CLAIM_MODAL.USER_ID"),
  email: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "common:EMAIL"),
  name: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "common:NAME"),
  userName: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "MAP_JWT_CLAIM_MODAL.USER_NAME"),
  roles: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "MAP_JWT_CLAIM_MODAL.ROLES"),
});

// JWT Input Section Component
interface JwtInputSectionProps {
  jwtToken: string;
  onTokenChange: (value: string) => void;
  validationError: string;
  onDecode: () => void;
}

const JwtInputSection: React.FC<JwtInputSectionProps> = ({
  jwtToken,
  onTokenChange,
  validationError,
  onDecode,
}) => {
  const { t } = useT(UTILITIES_IDP_AUTHENTICATION_MODULE);
  return (
  <div className="space-y-3">
    <p className="text-sm font-medium text-foreground">{t("MAP_JWT_CLAIM_MODAL.JSON_WEB_TOKEN_JWT")}</p>
    <Textarea
      value={jwtToken}
      onChange={(e) => onTokenChange(e.target.value)}
      placeholder={t("MAP_JWT_CLAIM_MODAL.PASTE_HERE")}
      className="h-[142px] w-full resize-none rounded-md border px-3 py-2 text-sm placeholder:align-top placeholder:text-muted-foreground"
    />
    {validationError && <p className="text-sm text-destructive">{validationError}</p>}
    <Button onClick={onDecode} variant="outline" className="w-full sm:w-auto">
      {t("MAP_JWT_CLAIM_MODAL.DECODE")}
    </Button>
  </div>
);
};

// Mapping Table Section Component
interface MappingTableSectionProps {
  hasDecodedJwt: boolean;
  hasExistingData: boolean;
  requiredProperties: readonly RequiredProperty[];
  decodedJwt: string[];
  mapping: Record<RequiredProperty, string>;
  onMappingChange: (property: RequiredProperty, value: string) => void;
}

const MappingTableSection: React.FC<MappingTableSectionProps> = ({
  hasDecodedJwt,
  hasExistingData,
  requiredProperties,
  decodedJwt,
  mapping,
  onMappingChange,
}) => {
  const { t } = useT(UTILITIES_IDP_AUTHENTICATION_MODULE);
  const existingMappedValues = useMemo(() => {
    const values = Object.values(mapping).filter((value) => value !== "");
    return Array.from(new Set(values));
  }, [mapping]);

  return (
    <div className="flex min-h-0 flex-1 flex-col space-y-3">
      <p className="border-t pt-3 text-sm font-medium text-foreground">{t("MAP_JWT_CLAIM_MODAL.MAPPING_TABLE")}</p>

      {!hasDecodedJwt && !hasExistingData && (
        <div className="flex flex-1 items-center justify-center py-10">
          <p className="text-sm text-muted-foreground">
            {t("MAP_JWT_CLAIM_MODAL.PLEASE_PASTE_A_VALID_JWT")}
          </p>
        </div>
      )}

      {(hasDecodedJwt || hasExistingData) && (
        <div className="flex-1 overflow-y-auto rounded-md">
          <Table className="min-w-full text-sm">
            <TableHeader className="sticky top-0 z-10 bg-background">
              <TableRow>
                <TableHead className="w-1/2 text-left font-semibold text-foreground">
                  {t("MAP_JWT_CLAIM_MODAL.JWT_KEY")}
                </TableHead>
                <TableHead className="w-1/2 text-left font-semibold text-foreground">
                  {t("MAP_JWT_CLAIM_MODAL.MAP_TO")}
                </TableHead>
              </TableRow>
            </TableHeader>

            <TableBody>
              {requiredProperties.map((property) => (
                <TableRow key={property}>
                  <TableCell className="border-b py-2 text-foreground">
                    {getPropertyLabels()[property]}
                  </TableCell>
                  <TableCell className="border-b py-2">
                    <Select
                      value={mapping[property]}
                      onValueChange={(value) => onMappingChange(property, value)}
                    >
                      <SelectTrigger className="border-0 text-foreground shadow-none focus:outline-none focus:ring-0 focus:ring-offset-0">
                        <SelectValue placeholder={t("MAP_JWT_CLAIM_MODAL.SELECT_MAPPING")} />
                      </SelectTrigger>
                      <SelectContent>
                        {(hasDecodedJwt ? decodedJwt : existingMappedValues).map((jwtKey) => (
                          <SelectItem key={jwtKey} value={jwtKey}>
                            {jwtKey}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
};

// Modal Footer Component
interface ModalFooterProps {
  onCancel: () => void;
  onSave: () => void;
  isSaveDisabled: boolean;
  isLoading: boolean;
}

const ModalFooter: React.FC<ModalFooterProps> = ({
  onCancel,
  onSave,
  isSaveDisabled,
  isLoading,
}) => {
  const { t } = useT(UTILITIES_IDP_AUTHENTICATION_MODULE);
  return (
  <div className="flex flex-col gap-2 pt-4 sm:flex-row sm:justify-end sm:gap-3">
    <Button onClick={onCancel} variant="outline" className="w-full sm:w-auto">
      {t("common:CANCEL")}
    </Button>
    <Button
      onClick={onSave}
      disabled={isSaveDisabled}
      className="w-full bg-primary text-primary-foreground sm:w-auto"
    >
      {isLoading ? t("MAP_JWT_CLAIM_MODAL.SAVING") : t("common:SAVE")}
    </Button>
  </div>
);
};

const MapJwtClaimModal: React.FC<MapJwtClaimModalProps> = ({ open, onOpenChange }) => {
  const { t } = useT(UTILITIES_IDP_AUTHENTICATION_MODULE);
  const [jwtToken, setJwtToken] = useState<string>("");
  const [decodedJwt, setDecodedJwt] = useState<string[]>([]);
  const [mapping, setMapping] = useState<Record<RequiredProperty, string>>({
    userId: "",
    email: "",
    name: "",
    userName: "",
    roles: "",
  });
  const [validationError, setValidationError] = useState<string>("");

  const { mutateAsync: saveJWTClaim, isPending: isLoading } = useAddJwtClaim();
  const projectKey = useProjectStore().selectedProject?.tenantId || "";

  const { data: existingJwtClaim, isLoading: isJwtClaimLoading } = useGetJwtClaim(
    {
      projectKey,
      itemId: "",
    },
    open,
  );

  const [prevJwtClaim, setPrevJwtClaim] = useState<typeof existingJwtClaim | undefined>(
    undefined,
  );
  if (existingJwtClaim && prevJwtClaim !== existingJwtClaim) {
    setPrevJwtClaim(existingJwtClaim);
    setMapping({
      userId: existingJwtClaim.userId || "",
      email: existingJwtClaim.email || "",
      name: existingJwtClaim.name || "",
      userName: existingJwtClaim.userName || "",
      roles: existingJwtClaim.roles || "",
    });
  }

  const hasDecodedJwt = useMemo(() => decodedJwt.length > 0, [decodedJwt.length]);
  const hasExistingData = useMemo(() => !!existingJwtClaim?.itemId, [existingJwtClaim]);
  const hasAtLeastOneFieldMapped = useMemo(
    () => REQUIRED_PROPERTIES.some((property) => mapping[property] !== ""),
    [mapping],
  );
  const isSaveDisabled = useMemo(
    () => isLoading || (!hasExistingData && (!hasDecodedJwt || !hasAtLeastOneFieldMapped)),
    [isLoading, hasExistingData, hasDecodedJwt, hasAtLeastOneFieldMapped],
  );

  const handleDecode = useCallback(() => {
    if (!jwtToken.trim()) {
      setValidationError(t("MAP_JWT_CLAIM_MODAL.JWT_IS_REQUIRED"));
      setDecodedJwt([]);
      return;
    }

    try {
      const result = jwtDecode<DecodedJwt>(jwtToken);
      const jwtKeys: string[] = [];

      const extractKeys = (
        obj: Record<string, unknown>,
        prefix: string = "",
        depth: number = 0,
      ) => {
        Object.keys(obj).forEach((key) => {
          const fullKey = prefix ? `${prefix}.${key}` : key;
          const value = obj[key];

          if (depth < 2 && value !== null && typeof value === "object" && !Array.isArray(value)) {
            extractKeys(value as Record<string, unknown>, fullKey, depth + 1);
          } else {
            jwtKeys.push(fullKey);
          }
        });
      };

      extractKeys(result);

      if (jwtKeys.length === 0) {
        setValidationError(t("MAP_JWT_CLAIM_MODAL.INVALID_JWT_TOKEN_NO_PROPERTIES"));
        setDecodedJwt([]);
        return;
      }

      setDecodedJwt(jwtKeys);
      setValidationError("");
      showSuccessToast({
        description: t("MAP_JWT_CLAIM_MODAL.JWT_DECODED_SUCCESSFULLY_YOU_CAN"),
      });
    } catch (error) {
      console.error("JWT decode error:", error);
      setValidationError(t("MAP_JWT_CLAIM_MODAL.INVALID_JWT_TOKEN"));
      setDecodedJwt([]);
    }
  }, [jwtToken, t]);

  const handleMappingChange = useCallback((property: RequiredProperty, value: string) => {
    setMapping((prev) => ({
      ...prev,
      [property]: value,
    }));
  }, []);

  const handleSubmit = useCallback(async () => {
    try {
      const payload: JwtClaimPayload = {
        userId: mapping.userId,
        email: mapping.email,
        name: mapping.name,
        userName: mapping.userName,
        roles: mapping.roles,
        projectKey,
        ...(existingJwtClaim?.itemId && { itemId: existingJwtClaim.itemId }),
      };

      const res = await saveJWTClaim(payload);

      if (res?.isSuccess) {
        showSuccessToast({ description: t("MAP_JWT_CLAIM_MODAL.JWT_CLAIM_SAVED_SUCCESSFULLY") });
        onOpenChange(false);
      } else {
        showErrorToast({ errors: t("MAP_JWT_CLAIM_MODAL.SOMETHING_WENT_WRONG") });
      }
    } catch (error) {
      showErrorToast({ errors: error });
    }
  }, [mapping, projectKey, existingJwtClaim, saveJWTClaim, onOpenChange, t]);

  const handleCancel = useCallback(() => {
    onOpenChange(false);
  }, [onOpenChange]);

  return (
    <Drawer direction="right" open={open} onOpenChange={onOpenChange} handleOnly>
      <DrawerContent
        className={cn(
          "inset-y-0 left-auto right-0 mt-0 h-full w-full rounded-none border-l bg-background p-6 md:w-[672px] [&>div:first-child]:hidden",
          "ease-in-out data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:slide-out-to-right data-[state=open]:slide-in-from-right",
        )}
        style={{ userSelect: "text" }}
      >
        <div className="flex flex-1 flex-col">
          <div className="flex items-center justify-between gap-4">
            <DrawerTitle className="text-lg font-semibold leading-none tracking-tight">
              {t("MAP_JWT_CLAIM_MODAL.MAP_JWT_CLAIM")}
            </DrawerTitle>
            <DrawerClose asChild>
              <button
                type="button"
                className="inline-flex h-8 w-8 items-center justify-center rounded-full text-muted-foreground hover:bg-muted"
                aria-label={t("MAP_JWT_CLAIM_MODAL.CLOSE_DRAWER")}
              >
                <X className="h-4 w-4" />
              </button>
            </DrawerClose>
          </div>

          {isJwtClaimLoading ? (
            <div className="mt-6 flex min-h-0 flex-1 flex-col space-y-6">
              <div className="space-y-3">
                <Skeleton className="h-5 w-48" />
                <Skeleton className="h-[142px] w-full" />
                <Skeleton className="h-10 w-24" />
              </div>
              <div className="flex-1 space-y-3">
                <Skeleton className="h-5 w-32" />
                <div className="space-y-2">
                  {REQUIRED_PROPERTIES.map((property) => (
                    <Skeleton key={property} className="h-10 w-full" />
                  ))}
                </div>
              </div>
            </div>
          ) : (
            <div className="mt-6 flex min-h-0 flex-1 flex-col space-y-6">
              <JwtInputSection
                jwtToken={jwtToken}
                onTokenChange={setJwtToken}
                validationError={validationError}
                onDecode={handleDecode}
              />

              <MappingTableSection
                hasDecodedJwt={hasDecodedJwt}
                hasExistingData={hasExistingData}
                requiredProperties={REQUIRED_PROPERTIES}
                decodedJwt={decodedJwt}
                mapping={mapping}
                onMappingChange={handleMappingChange}
              />
            </div>
          )}

          <ModalFooter
            onCancel={handleCancel}
            onSave={handleSubmit}
            isSaveDisabled={isSaveDisabled || isJwtClaimLoading}
            isLoading={isLoading}
          />
        </div>
      </DrawerContent>
    </Drawer>
  );
};

export default MapJwtClaimModal;
