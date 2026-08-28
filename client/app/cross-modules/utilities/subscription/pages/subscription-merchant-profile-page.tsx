import { useEffect, useRef, useState } from "react";
import { AlertCircle, Building2, CheckCircle2, Upload, X } from "lucide-react";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { ModuleName } from "@/constants/modules.constants";
import {
  useGetPreSignedUrlForUpload,
  useLazyGetFile,
  useUploadFile,
} from "@blocks-storage/hooks/use-storage-file";
import { storageService } from "@blocks-storage/services/storage.service";
import { toast } from "@/hooks/use-toast";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import { useSubscriptionLink } from "../hooks/use-subscription-link";
import {
  useMerchantProfile,
  useUpdateMerchantProfile,
} from "../hooks/use-merchant-profile";
import type { SubscriptionMerchantProfile } from "../models/subscription-billing.model";

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
  // The seller is the tenant, so nothing on this page is scoped to an organization. The link out of
  // it still carries whichever organization the catalogue was being read as, so a detour through
  // here does not silently reset it.
  const subscriptionLink = useSubscriptionLink();
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
        title: "That file type isn't supported",
        description: "Upload a PNG, JPEG or SVG.",
      });

      return;
    }

    if (file.size > MAX_LOGO_BYTES) {
      toast({
        variant: "destructive",
        title: "That logo is too large",
        description: `Keep it under ${Math.round(MAX_LOGO_BYTES / 1024)} KB.`,
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
        throw new Error("Could not get an upload URL.");
      }

      await uploadFile({ url: preSigned.uploadUrl, file });

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
        title: "The logo could not be uploaded",
        description: uploadError instanceof Error ? uploadError.message : "Something went wrong.",
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
      },
      { onSuccess: () => setSaved(true) },
    );
  };

  const saveError = update.error instanceof Error ? update.error.message : null;

  return (
    <div className="flex flex-col gap-6">
      <SubscriptionPlanPageHeader
        title="Merchant profile"
        description="The legal identity this tenant issues its invoices and credit notes under."
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
                ? "Issuing under the deployment's configured identity."
                : "No seller is named yet."}
            </p>
            <p className="text-muted-foreground">
              {profile.isComplete
                ? "Every tenant on this deployment shares that identity. Set one here so this tenant's documents name the company that is actually selling."
                : "A paid subscription cannot start until a seller is named, because an invoice has to state who issued it."}
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
            <p className="font-medium">This tenant issues under its own identity.</p>
            <p className="text-muted-foreground">
              Changes appear on documents issued from now on. Invoices already sent keep the seller
              they were issued with.
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
            <Label htmlFor="merchantLegalName">Legal name</Label>
            <Input
              id="merchantLegalName"
              value={form.legalName}
              onChange={(event) => set("legalName")(event.target.value)}
              placeholder="Northwind Software GmbH"
            />
            <p className="text-xs text-muted-foreground">
              The registered name of the selling entity, as it must appear on an invoice.
            </p>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantDisplayName">Trading name</Label>
            <Input
              id="merchantDisplayName"
              value={form.displayName}
              onChange={(event) => set("displayName")(event.target.value)}
              placeholder="Northwind"
            />
            <p className="text-xs text-muted-foreground">
              Optional. Falls back to the legal name.
            </p>
          </div>

          <div className="flex flex-col gap-2 sm:col-span-2">
            <Label htmlFor="merchantLine1">Address</Label>
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
              placeholder="Second line, if there is one"
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantPostalCode">Postal code</Label>
            <Input
              id="merchantPostalCode"
              value={form.postalCode}
              onChange={(event) => set("postalCode")(event.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantCity">City</Label>
            <Input
              id="merchantCity"
              value={form.city}
              onChange={(event) => set("city")(event.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantRegion">State or region</Label>
            <Input
              id="merchantRegion"
              value={form.region}
              onChange={(event) => set("region")(event.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantCountryCode">Country code</Label>
            <Input
              id="merchantCountryCode"
              value={form.countryCode}
              onChange={(event) => set("countryCode")(event.target.value)}
              maxLength={2}
              placeholder="CH"
            />
            <p className="text-xs text-muted-foreground">Two letters, ISO 3166-1.</p>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantTaxRegistrationId">Tax or VAT registration</Label>
            <Input
              id="merchantTaxRegistrationId"
              value={form.taxRegistrationId}
              onChange={(event) => set("taxRegistrationId")(event.target.value)}
              placeholder="CHE-123.456.789"
            />
            <p className="text-xs text-muted-foreground">
              The seller&apos;s own registration. Required in some jurisdictions and meaningless in
              others, so nothing here insists on it.
            </p>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantSupportEmail">Support email</Label>
            <Input
              id="merchantSupportEmail"
              type="email"
              value={form.supportEmail}
              onChange={(event) => set("supportEmail")(event.target.value)}
              placeholder="billing@northwind.example"
            />
            <p className="text-xs text-muted-foreground">
              Where a subscriber replies about a charge.
            </p>
          </div>

          <div className="flex flex-col gap-2 sm:col-span-2">
            <Label htmlFor="merchantPaymentInstructions">Payment instructions</Label>
            <Textarea
              id="merchantPaymentInstructions"
              value={form.paymentInstructions}
              onChange={(event) => set("paymentInstructions")(event.target.value)}
              rows={3}
              placeholder="Bank details, terms, a remittance reference."
            />
            <p className="text-xs text-muted-foreground">
              Printed verbatim under the totals on every document.
            </p>
          </div>
        </div>
      </Card>

      <Card className="flex flex-col gap-5 p-5">
        <div>
          <h2 className="text-sm font-medium">Invoice branding</h2>
          <p className="text-xs text-muted-foreground">
            The layout is fixed for every tenant. Only the logo and these two colors change — and
            only for documents issued after they are saved; a document already sent keeps the
            branding it was issued with.
          </p>
        </div>

        <div className="flex items-start gap-4">
          <div className="flex h-16 w-16 shrink-0 items-center justify-center rounded-md border bg-muted/30">
            {logoPreviewUrl ? (
              <img
                src={logoPreviewUrl}
                alt="Invoice logo"
                className="h-full w-full rounded-md object-contain"
              />
            ) : (
              <span className="text-xs text-muted-foreground">No logo</span>
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
                {uploadingLogo ? "Uploading…" : form.logoFileId ? "Replace logo" : "Upload logo"}
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
                  Remove
                </Button>
              )}
            </div>
            <p className="text-xs text-muted-foreground">
              PNG, JPEG or SVG, under {Math.round(MAX_LOGO_BYTES / 1024)} KB. Without one, documents
              show this name as text instead — a missing or unreadable logo never blocks a document.
            </p>
          </div>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantPrimaryColor">Primary color</Label>
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
            <p className="text-xs text-muted-foreground">Headings and the total due.</p>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="merchantAccentColor">Accent color</Label>
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
            <p className="text-xs text-muted-foreground">Trial and note backgrounds.</p>
          </div>
        </div>

        {saveError && (
          <p className="text-sm text-destructive" data-testid="merchant-error">
            {saveError}
          </p>
        )}

        {saved && (
          <p className="text-sm text-emerald-700" data-testid="merchant-saved">
            Saved. Documents issued from now on name this seller.
          </p>
        )}

        <div className="flex items-center gap-3">
          <Button onClick={submit} disabled={isLoading || update.isPending}>
            {update.isPending ? "Saving…" : "Save merchant profile"}
          </Button>
          <span className="text-xs text-muted-foreground">
            Accepted from the platform console only — an invoice names a seller in law.
          </span>
        </div>
      </Card>
    </div>
  );
};
