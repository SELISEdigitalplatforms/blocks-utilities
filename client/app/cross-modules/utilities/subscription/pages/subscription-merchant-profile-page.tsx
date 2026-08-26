import { useState } from "react";
import { AlertCircle, Building2, CheckCircle2 } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import { useSubscriptionLink } from "../hooks/use-subscription-link";
import {
  useMerchantProfile,
  useUpdateMerchantProfile,
} from "../hooks/use-merchant-profile";
import type { SubscriptionMerchantProfile } from "../models/subscription-billing.model";

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

  // Adjusted during render rather than in an effect, which is the pattern React documents for
  // resetting state when the thing being edited changes. An effect would render the stale form once
  // first and cascade a second render on every arrival.
  if (profile && loadedIdentity !== identityOf(profile)) {
    setLoadedIdentity(identityOf(profile));
    setForm(toForm(profile));
  }

  const set = (field: keyof MerchantForm) => (value: string) => {
    setForm((current) => ({ ...current, [field]: value }));
    setSaved(false);
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
