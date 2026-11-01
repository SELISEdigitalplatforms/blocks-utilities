import { useEffect, useRef, useState } from "react";
import { AlertCircle, Building2, CheckCircle2, Upload, X } from "lucide-react";
import { useParams } from "react-router";
import { useProjectStore, useT, tStatic } from "@seliseblocks/genesis-os";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { ModuleName } from "@/constants/modules.constants";
import {
  useCompleteUpload,
  useGetPreSignedUrlForUpload,
  useLazyGetFile,
  useUploadFile,
} from "@blocks-storage/hooks/use-storage-file";
import { storageService } from "@blocks-storage/services/storage.service";
import { toast } from "@/hooks/use-toast";
import { RichText } from "../components/rich-text";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import { useSubscriptionLink } from "../hooks/use-subscription-link";
import {
  useMerchantProfile,
  useUpdateMerchantProfile,
} from "../hooks/use-merchant-profile";
import type {
  SubscriptionMerchantProfile,
  SubscriptionMerchantProfilePaymentProvider,
  SubscriptionPaymentProviderName,
} from "../models/subscription-billing.model";

const UTILITIES_SUBSCRIPTION_PAGES_MODULE = "utilities.subscription.pages";

const PAYMENT_PROVIDERS: {
  name: SubscriptionPaymentProviderName;
  label: string;
}[] = [
  { name: "STRIPE", label: "Stripe" },
  { name: "ADYEN-ONLINE", label: "Adyen" },
];

/** A short, human phrase for each readiness outcome — see the server's own enum remarks. */
const readinessLabel = (
  status: SubscriptionMerchantProfilePaymentProvider["status"] | undefined,
): string => {
  switch (status) {
    case "Ready":
      return tStatic(UTILITIES_SUBSCRIPTION_PAGES_MODULE, "MERCHANT_PROFILE_PAGE.READINESS_READY");
    case "NotConfigured":
      return tStatic(UTILITIES_SUBSCRIPTION_PAGES_MODULE, "MERCHANT_PROFILE_PAGE.READINESS_NOT_CONFIGURED");
    case "Disabled":
      return tStatic(UTILITIES_SUBSCRIPTION_PAGES_MODULE, "common:DISABLED");
    case "Misconfigured":
      return tStatic(UTILITIES_SUBSCRIPTION_PAGES_MODULE, "MERCHANT_PROFILE_PAGE.READINESS_MISCONFIGURED");
    case "CredentialsUnavailable":
      return tStatic(UTILITIES_SUBSCRIPTION_PAGES_MODULE, "MERCHANT_PROFILE_PAGE.READINESS_CREDENTIALS_UNAVAILABLE");
    case "Unsupported":
      return tStatic(UTILITIES_SUBSCRIPTION_PAGES_MODULE, "MERCHANT_PROFILE_PAGE.READINESS_UNSUPPORTED");
    default:
      return tStatic(UTILITIES_SUBSCRIPTION_PAGES_MODULE, "MERCHANT_PROFILE_PAGE.READINESS_UNKNOWN");
  }
};

/** Matches the server's own allow-list — see FinancialDocumentLogoResolver.SniffMimeType. */
const ALLOWED_LOGO_TYPES = ["image/png", "image/jpeg", "image/jpg", "image/svg+xml"];

/** Matches FinancialDocumentLogoResolver.MaxLogoBytes — kept in sync by eye, not by import. */
const MAX_LOGO_BYTES = 512 * 1024;

const DEFAULT_PRIMARY_COLOR = "#17365D";
const DEFAULT_ACCENT_COLOR = "#D9E7F5";

/**
 * The loaded profile's identity, so the form can tell a refetch from a different answer arriving.
 *
 * Includes whether the values are still the configured fallback, because saving for the first time
 * changes that flag and nothing else — and the form has to reload on it or it would keep showing the
 * inherited banner after the tenant has its own.
 */
const identityOf = (profile: SubscriptionMerchantProfile): string =>
  `${profile.lastUpdatedDateUtc ?? ""}:${profile.isInheritedFromConfiguration}`;

interface MerchantForm {
  legalName: string;
  displayName: string;
  line1: string;
  line2: string;
  city: string;
  region: string;
  postalCode: string;
  countryCode: string;
  taxRegistrationId: string;
  supportEmail: string;
  paymentInstructions: string;
  logoFileId: string;
  primaryColor: string;
  accentColor: string;
  paymentProviderName: SubscriptionPaymentProviderName;
}

const emptyForm: MerchantForm = {
  legalName: "",
  displayName: "",
  line1: "",
  line2: "",
  city: "",
  region: "",
  postalCode: "",
  countryCode: "",
  taxRegistrationId: "",
  supportEmail: "",
  paymentInstructions: "",
  logoFileId: "",
  primaryColor: DEFAULT_PRIMARY_COLOR,
  accentColor: DEFAULT_ACCENT_COLOR,
  paymentProviderName: "STRIPE",
};

const toForm = (profile: SubscriptionMerchantProfile): MerchantForm => ({
  legalName: profile.legalName ?? "",
  displayName: profile.displayName ?? "",
  line1: profile.address?.line1 ?? "",
  line2: profile.address?.line2 ?? "",
  city: profile.address?.city ?? "",
  region: profile.address?.region ?? "",
  postalCode: profile.address?.postalCode ?? "",
  countryCode: profile.address?.countryCode ?? "",
  taxRegistrationId: profile.taxRegistrationId ?? "",
  supportEmail: profile.supportEmail ?? "",
  paymentInstructions: profile.paymentInstructions ?? "",
  logoFileId: profile.logoFileId ?? "",
  // The native color input needs a real value at all times; a document with no color set renders
  // from this same shared default, so showing it here is what the form will actually save if the
  // fields are left untouched, not a placeholder standing in for "unset".
  primaryColor: profile.primaryColor ?? DEFAULT_PRIMARY_COLOR,
  accentColor: profile.accentColor ?? DEFAULT_ACCENT_COLOR,
  paymentProviderName: profile.paymentProviderName ?? "STRIPE",
});

/**
 * Who this tenant issues its invoices and credit notes under.
 *
 * The selling half of the pair; the billing profile is the buying half. Separate pages because they
 * are set by different people at different times: a subscriber fills in their own details when they
 * reach checkout, while the seller is set once, by whoever runs the platform, and the server accepts
 * it from the console alone.
 */
export const SubscriptionMerchantProfilePage = () => {
  const { t } = useT(UTILITIES_SUBSCRIPTION_PAGES_MODULE);
  // The seller is the tenant, so nothing on this page is scoped to an organization. The link out of
  // it still carries whichever organization the catalogue was being read as, so a detour through
  // here does not silently reset it.
  const subscriptionLink = useSubscriptionLink();
  const { itemId } = useParams();
  const paymentProvidersLink = `/app/${itemId ?? ""}/payment/providers`;
  const { data: profile, isLoading, error } = useMerchantProfile();
  const update = useUpdateMerchantProfile();
  const [form, setForm] = useState<MerchantForm>(emptyForm);
  const [loadedIdentity, setLoadedIdentity] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [logoPreviewUrl, setLogoPreviewUrl] = useState<string | null>(null);
  const [uploadingLogo, setUploadingLogo] = useState(false);
  const logoInputRef = useRef<HTMLInputElement>(null);
  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const { mutateAsync: getPreSignedUrl } = useGetPreSignedUrlForUpload();
  const { mutateAsync: uploadFile } = useUploadFile();
  const { mutateAsync: completeUpload } = useCompleteUpload();
  const { fetchFile } = useLazyGetFile();

  // Adjusted during render rather than in an effect, which is the pattern React documents for
  // resetting state when the thing being edited changes. An effect would render the stale form once
  // first and cascade a second render on every arrival.
  if (profile && loadedIdentity !== identityOf(profile)) {
    setLoadedIdentity(identityOf(profile));
    setForm(toForm(profile));
    // A fresh id, not yet resolved to a viewable URL — fetched below, separately, because that
    // fetch is genuinely asynchronous and cannot happen inside a render.
    setLogoPreviewUrl(null);
  }

  // The preview is the one thing here that cannot be adjusted synchronously during render: unlike
  // the rest of the form, which is copied straight off the loaded profile, a display URL for an
  // already-uploaded logo has to be resolved from its file id with its own request.
  useEffect(() => {
    if (!form.logoFileId || !tenantId) {
      return;
    }

    let cancelled = false;

    fetchFile({ itemId: form.logoFileId, projectKey: tenantId })
      .then((file) => {
        if (!cancelled) {
          setLogoPreviewUrl(file.url);
        }
      })
      .catch(() => {
        // The same "branding must not block anything" rule as the server side: a preview that
        // cannot be fetched just means no preview, not an error banner over the whole form.
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [form.logoFileId, tenantId]);

  const set = (field: keyof MerchantForm) => (value: string) => {
    setForm((current) => ({ ...current, [field]: value }));
    setSaved(false);
  };

  const uploadLogo = async (file: File) => {
    if (!ALLOWED_LOGO_TYPES.includes(file.type)) {
      toast({
        variant: "destructive",
        title: t("MERCHANT_PROFILE_PAGE.LOGO_TYPE_TITLE"),
        description: t("MERCHANT_PROFILE_PAGE.LOGO_TYPE_DESCRIPTION"),
      });

      return;
    }

    if (file.size > MAX_LOGO_BYTES) {
      toast({
        variant: "destructive",
        title: t("MERCHANT_PROFILE_PAGE.LOGO_SIZE_TITLE"),
        description: t("MERCHANT_PROFILE_PAGE.LOGO_SIZE_DESCRIPTION", {
          size: Math.round(MAX_LOGO_BYTES / 1024),
        }),
      });

      return;
    }

    setUploadingLogo(true);

    try {
      const preSigned = await getPreSignedUrl({
        accessModifier: "Public",
        configurationName: "Default",
        name: file.name,
        projectKey: tenantId,
        tags: "",
        metaData: "",
        parentDirectoryId: "",
        moduleName: ModuleName.IAMCloud,
      });

      if (!preSigned.isSuccess) {
        throw new Error(t("MERCHANT_PROFILE_PAGE.UPLOAD_URL_FAILED"));
      }

      await uploadFile({ url: preSigned.uploadUrl, file });

      if (preSigned.uploadCompletionRequired) {
        const completion = await completeUpload({
          fileId: preSigned.fileId,
          fileVersionId: preSigned.fileVersionId ?? "",
        });
        if (completion.verificationStatus !== "Verified") {
          throw new Error(completion.rejectionReason ?? t("MERCHANT_PROFILE_PAGE.LOGO_VERIFICATION_FAILED"));
        }
      }

      const uploaded = await storageService.file.getFileByFileId({
        itemId: preSigned.fileId,
        projectKey: tenantId,
      });

      setForm((current) => ({ ...current, logoFileId: uploaded.itemId }));
      setLogoPreviewUrl(uploaded.url);
      setSaved(false);
    } catch (uploadError) {
      toast({
        variant: "destructive",
        title: t("MERCHANT_PROFILE_PAGE.LOGO_UPLOAD_FAILED"),
        description:
          uploadError instanceof Error
            ? uploadError.message
            : t("MERCHANT_PROFILE_PAGE.SOMETHING_WENT_WRONG"),
      });
    } finally {
      setUploadingLogo(false);
    }
  };

  const submit = () => {
    setSaved(false);
    update.mutate(
      {
        legalName: form.legalName.trim(),
        displayName: form.displayName.trim() || null,
        // Sent as an object even when every line is blank; the server stores that as no address, so
        // clearing the fields here actually clears it rather than leaving the old one in place.
        address: {
          line1: form.line1.trim() || null,
          line2: form.line2.trim() || null,
          city: form.city.trim() || null,
          region: form.region.trim() || null,
          postalCode: form.postalCode.trim() || null,
          countryCode: form.countryCode.trim().toUpperCase() || null,
        },
        taxRegistrationId: form.taxRegistrationId.trim() || null,
        supportEmail: form.supportEmail.trim() || null,
        paymentInstructions: form.paymentInstructions.trim() || null,
        logoFileId: form.logoFileId.trim() || null,
        primaryColor: form.primaryColor || null,
        accentColor: form.accentColor || null,
        paymentProviderName: form.paymentProviderName,
      },
      { onSuccess: () => setSaved(true) },
    );
  };

  const saveError = update.error instanceof Error ? update.error.message : null;

  const selectedProviderStatus = profile?.paymentProviders.find(
    (provider) => provider.name === form.paymentProviderName,
  )?.status;
  const selectedProviderNotReady = Boolean(profile) && selectedProviderStatus !== "Ready";
  const providerChanged = Boolean(
    profile && form.paymentProviderName !== (profile.paymentProviderName ?? "STRIPE"),
  );

  const idleLogoLabel = form.logoFileId
    ? t("MERCHANT_PROFILE_PAGE.REPLACE_LOGO")
    : t("MERCHANT_PROFILE_PAGE.UPLOAD_LOGO");
  const logoButtonLabel = uploadingLogo ? t("MERCHANT_PROFILE_PAGE.UPLOADING") : idleLogoLabel;

  return (
    <div className="flex flex-col gap-6">
      <SubscriptionPlanPageHeader
        title={t("MERCHANT_PROFILE_PAGE.TITLE")}
        description={t("MERCHANT_PROFILE_PAGE.DESCRIPTION")}
        backTo={subscriptionLink("plans")}
        icon={<Building2 className="h-6 w-6" />}
      />

      {profile?.isInheritedFromConfiguration && (
        <Card
          className="flex items-start gap-3 border-amber-300 bg-amber-50 p-4"
          data-testid="merchant-inherited"
        >
          <AlertCircle className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" />
          <div className="text-sm">
            <p className="font-medium">
              {profile.isComplete
                ? t("MERCHANT_PROFILE_PAGE.INHERITED_TITLE")
                : t("MERCHANT_PROFILE_PAGE.NO_SELLER_TITLE")}
            </p>
            <p className="text-muted-foreground">
              {profile.isComplete
                ? t("MERCHANT_PROFILE_PAGE.INHERITED_HINT")
                : t("MERCHANT_PROFILE_PAGE.NO_SELLER_HINT")}
            </p>
          </div>
        </Card>
      )}

      {profile && !profile.isInheritedFromConfiguration && (
        <Card
          className="flex items-start gap-3 border-emerald-300 bg-emerald-50 p-4"
          data-testid="merchant-own"
        >
          <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-emerald-600" />
          <div className="text-sm">
            <p className="font-medium">{t("MERCHANT_PROFILE_PAGE.OWN_TITLE")}</p>
            <p className="text-muted-foreground">
              {t("MERCHANT_PROFILE_PAGE.OWN_HINT")}
            </p>
          </div>
        </Card>
      )}

      {error instanceof Error && (
        <Card className="border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
          {error.message}
        </Card>
      )}

      <Card className="flex flex-col gap-5 p-5">
        <div className="grid gap-4 sm:grid-cols-2">
          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantLegalName">{t("MERCHANT_PROFILE_PAGE.LEGAL_NAME")}</Label>
            <Input
              id="merchantLegalName"
              value={form.legalName}
              onChange={(event) => set("legalName")(event.target.value)}
              placeholder="Northwind Software GmbH"
            />
            <p className="text-xs text-muted-foreground">
              {t("MERCHANT_PROFILE_PAGE.LEGAL_NAME_HINT")}
            </p>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantDisplayName">{t("MERCHANT_PROFILE_PAGE.TRADING_NAME")}</Label>
            <Input
              id="merchantDisplayName"
              value={form.displayName}
              onChange={(event) => set("displayName")(event.target.value)}
              placeholder="Northwind"
            />
            <p className="text-xs text-muted-foreground">
              {t("MERCHANT_PROFILE_PAGE.TRADING_NAME_HINT")}
            </p>
          </div>

          <div className="flex flex-col gap-2 sm:col-span-2">
            <Label htmlFor="merchantLine1">{t("MERCHANT_PROFILE_PAGE.ADDRESS")}</Label>
            <Input
              id="merchantLine1"
              value={form.line1}
              onChange={(event) => set("line1")(event.target.value)}
              placeholder="1 Bahnhofstrasse"
            />
            <Input
              id="merchantLine2"
              value={form.line2}
              onChange={(event) => set("line2")(event.target.value)}
              placeholder={t("MERCHANT_PROFILE_PAGE.ADDRESS_LINE2_PLACEHOLDER")}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantPostalCode">{t("MERCHANT_PROFILE_PAGE.POSTAL_CODE")}</Label>
            <Input
              id="merchantPostalCode"
              value={form.postalCode}
              onChange={(event) => set("postalCode")(event.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantCity">{t("MERCHANT_PROFILE_PAGE.CITY")}</Label>
            <Input
              id="merchantCity"
              value={form.city}
              onChange={(event) => set("city")(event.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantRegion">{t("MERCHANT_PROFILE_PAGE.REGION")}</Label>
            <Input
              id="merchantRegion"
              value={form.region}
              onChange={(event) => set("region")(event.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantCountryCode">{t("MERCHANT_PROFILE_PAGE.COUNTRY_CODE")}</Label>
            <Input
              id="merchantCountryCode"
              value={form.countryCode}
              onChange={(event) => set("countryCode")(event.target.value)}
              maxLength={2}
              placeholder="CH"
            />
            <p className="text-xs text-muted-foreground">{t("MERCHANT_PROFILE_PAGE.COUNTRY_CODE_HINT")}</p>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantTaxRegistrationId">{t("MERCHANT_PROFILE_PAGE.TAX_REGISTRATION")}</Label>
            <Input
              id="merchantTaxRegistrationId"
              value={form.taxRegistrationId}
              onChange={(event) => set("taxRegistrationId")(event.target.value)}
              placeholder="CHE-123.456.789"
            />
            <p className="text-xs text-muted-foreground">
              {t("MERCHANT_PROFILE_PAGE.TAX_REGISTRATION_HINT")}
            </p>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantSupportEmail">{t("MERCHANT_PROFILE_PAGE.SUPPORT_EMAIL")}</Label>
            <Input
              id="merchantSupportEmail"
              type="email"
              value={form.supportEmail}
              onChange={(event) => set("supportEmail")(event.target.value)}
              placeholder="billing@northwind.example"
            />
            <p className="text-xs text-muted-foreground">
              {t("MERCHANT_PROFILE_PAGE.SUPPORT_EMAIL_HINT")}
            </p>
          </div>

          <div className="flex flex-col gap-2 sm:col-span-2">
            <Label htmlFor="merchantPaymentInstructions">{t("MERCHANT_PROFILE_PAGE.PAYMENT_INSTRUCTIONS")}</Label>
            <Textarea
              id="merchantPaymentInstructions"
              value={form.paymentInstructions}
              onChange={(event) => set("paymentInstructions")(event.target.value)}
              rows={3}
              placeholder={t("MERCHANT_PROFILE_PAGE.PAYMENT_INSTRUCTIONS_PLACEHOLDER")}
            />
            <p className="text-xs text-muted-foreground">
              {t("MERCHANT_PROFILE_PAGE.PAYMENT_INSTRUCTIONS_HINT")}
            </p>
          </div>
        </div>
      </Card>

      <Card className="flex flex-col gap-5 p-5" data-testid="merchant-payment-provider">
        <div>
          <h2 className="text-sm font-medium">{t("MERCHANT_PROFILE_PAGE.PROVIDER_HEADING")}</h2>
          <p className="text-xs text-muted-foreground">
            {t("MERCHANT_PROFILE_PAGE.PROVIDER_HINT")}
          </p>
        </div>

        <div className="grid gap-3 sm:grid-cols-2">
          {PAYMENT_PROVIDERS.map((candidate) => {
            const status = profile?.paymentProviders.find(
              (provider) => provider.name === candidate.name,
            )?.status;
            const ready = status === "Ready";
            const selected = form.paymentProviderName === candidate.name;

            return (
              <button
                key={candidate.name}
                type="button"
                data-testid={`merchant-payment-provider-${candidate.name}`}
                onClick={() => {
                  setForm((current) => ({ ...current, paymentProviderName: candidate.name }));
                  setSaved(false);
                }}
                className={`flex flex-col gap-2 rounded-md border p-4 text-left transition-colors ${
                  selected ? "border-primary ring-1 ring-primary" : "border-input"
                }`}
              >
                <div className="flex items-center justify-between">
                  <span className="font-medium">{candidate.label}</span>
                  <Badge variant={ready ? "success" : "secondary"}>
                    {readinessLabel(status)}
                  </Badge>
                </div>
                {!ready && (
                  <p className="text-xs text-muted-foreground">
                    <RichText
                      text={t("MERCHANT_PROFILE_PAGE.PROVIDER_SET_UP")}
                      href={paymentProvidersLink}
                      linkClassName="underline"
                    />
                  </p>
                )}
              </button>
            );
          })}
        </div>

        {providerChanged && (
          <p className="text-xs text-muted-foreground" data-testid="merchant-payment-provider-changed">
            {t("MERCHANT_PROFILE_PAGE.PROVIDER_CHANGED")}
          </p>
        )}
      </Card>

      <Card className="flex flex-col gap-5 p-5">
        <div>
          <h2 className="text-sm font-medium">{t("MERCHANT_PROFILE_PAGE.BRANDING_HEADING")}</h2>
          <p className="text-xs text-muted-foreground">
            {t("MERCHANT_PROFILE_PAGE.BRANDING_HINT")}
          </p>
        </div>

        <div className="flex items-start gap-4">
          <div className="flex h-16 w-16 shrink-0 items-center justify-center rounded-md border bg-muted/30">
            {logoPreviewUrl ? (
              <img
                src={logoPreviewUrl}
                alt={t("MERCHANT_PROFILE_PAGE.LOGO_ALT")}
                className="h-full w-full rounded-md object-contain"
              />
            ) : (
              <span className="text-xs text-muted-foreground">{t("MERCHANT_PROFILE_PAGE.NO_LOGO")}</span>
            )}
          </div>

          <div className="flex flex-col gap-2">
            <input
              ref={logoInputRef}
              type="file"
              accept={ALLOWED_LOGO_TYPES.join(",")}
              className="hidden"
              onChange={(event) => {
                const file = event.target.files?.[0];
                event.target.value = "";

                if (file) {
                  uploadLogo(file);
                }
              }}
            />
            <div className="flex gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={uploadingLogo}
                onClick={() => logoInputRef.current?.click()}
              >
                <Upload className="mr-2 h-4 w-4" />
                {logoButtonLabel}
              </Button>
              {form.logoFileId && (
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => {
                    setForm((current) => ({ ...current, logoFileId: "" }));
                    setLogoPreviewUrl(null);
                    setSaved(false);
                  }}
                >
                  <X className="mr-2 h-4 w-4" />
                  {t("common:REMOVE")}
                </Button>
              )}
            </div>
            <p className="text-xs text-muted-foreground">
              {t("MERCHANT_PROFILE_PAGE.LOGO_HINT", { size: Math.round(MAX_LOGO_BYTES / 1024) })}
            </p>
          </div>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantPrimaryColor">{t("MERCHANT_PROFILE_PAGE.PRIMARY_COLOR")}</Label>
            <div className="flex items-center gap-2">
              <input
                id="merchantPrimaryColor"
                type="color"
                value={form.primaryColor}
                onChange={(event) => set("primaryColor")(event.target.value.toUpperCase())}
                className="h-9 w-12 shrink-0 cursor-pointer rounded border"
              />
              <Input
                value={form.primaryColor}
                onChange={(event) => set("primaryColor")(event.target.value)}
                maxLength={7}
              />
            </div>
            <p className="text-xs text-muted-foreground">{t("MERCHANT_PROFILE_PAGE.PRIMARY_COLOR_HINT")}</p>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantAccentColor">{t("MERCHANT_PROFILE_PAGE.ACCENT_COLOR")}</Label>
            <div className="flex items-center gap-2">
              <input
                id="merchantAccentColor"
                type="color"
                value={form.accentColor}
                onChange={(event) => set("accentColor")(event.target.value.toUpperCase())}
                className="h-9 w-12 shrink-0 cursor-pointer rounded border"
              />
              <Input
                value={form.accentColor}
                onChange={(event) => set("accentColor")(event.target.value)}
                maxLength={7}
              />
            </div>
            <p className="text-xs text-muted-foreground">{t("MERCHANT_PROFILE_PAGE.ACCENT_COLOR_HINT")}</p>
          </div>
        </div>

        {saveError && (
          <p className="text-sm text-destructive" data-testid="merchant-error">
            {saveError}
          </p>
        )}

        {saved && (
          <p className="text-sm text-emerald-700" data-testid="merchant-saved">
            {t("MERCHANT_PROFILE_PAGE.SAVED")}
          </p>
        )}

        <div className="flex items-center gap-3">
          <Button
            onClick={submit}
            disabled={isLoading || update.isPending || selectedProviderNotReady}
          >
            {update.isPending ? t("MERCHANT_PROFILE_PAGE.SAVING") : t("MERCHANT_PROFILE_PAGE.SAVE")}
          </Button>
          <span className="text-xs text-muted-foreground">
            {selectedProviderNotReady
              ? t("MERCHANT_PROFILE_PAGE.PROVIDER_NOT_READY")
              : t("MERCHANT_PROFILE_PAGE.CONSOLE_ONLY")}
          </span>
        </div>
      </Card>
    </div>
  );
};
