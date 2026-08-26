import { useState } from "react";
import { AlertCircle, CheckCircle2, ReceiptText } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import { useBillingProfile, useUpdateBillingProfile } from "../hooks/use-billing-profile";
import { useOrganizationScope } from "../hooks/use-organization-scope";
import type { SubscriptionBillingProfile } from "../models/subscription-billing.model";
import { describeMissingProfileFields } from "../utilities/financial-document-format";

/**
 * The loaded profile's identity, so the form can tell "this is the same profile I am editing" from
 * "a different one arrived".
 *
 * Includes the last-updated stamp, so a profile saved elsewhere reloads while one merely refetched
 * does not. That is what keeps a background refetch from discarding what somebody is typing.
 */
const identityOf = (profile: SubscriptionBillingProfile): string =>
  `${profile.organizationId}:${profile.lastUpdatedDateUtc ?? ""}`;

interface ProfileForm {
  legalName: string;
  displayName: string;
  billingContactName: string;
  billingContactEmail: string;
  line1: string;
  line2: string;
  city: string;
  region: string;
  postalCode: string;
  countryCode: string;
  taxRegistrationId: string;
}

const emptyForm: ProfileForm = {
  legalName: "",
  displayName: "",
  billingContactName: "",
  billingContactEmail: "",
  line1: "",
  line2: "",
  city: "",
  region: "",
  postalCode: "",
  countryCode: "",
  taxRegistrationId: "",
};

const toForm = (profile: SubscriptionBillingProfile): ProfileForm => ({
  legalName: profile.legalName ?? "",
  displayName: profile.displayName ?? "",
  billingContactName: profile.billingContactName ?? "",
  billingContactEmail: profile.billingContactEmail ?? "",
  line1: profile.address?.line1 ?? "",
  line2: profile.address?.line2 ?? "",
  city: profile.address?.city ?? "",
  region: profile.address?.region ?? "",
  postalCode: profile.address?.postalCode ?? "",
  countryCode: profile.address?.countryCode ?? "",
  taxRegistrationId: profile.taxRegistrationId ?? "",
});

/**
 * Who this organization's invoices are addressed to.
 *
 * The page exists because the server refuses a paid subscription without it, and the only useful
 * moment to ask is before the customer reaches a checkout that would turn them away. So the missing
 * fields are stated up front rather than discovered as a validation error on the next screen.
 */
export const SubscriptionBillingProfilePage = () => {
  const organizationId = useOrganizationScope();
  const { data: profile, isLoading, error } = useBillingProfile(organizationId);
  const update = useUpdateBillingProfile();
  const [form, setForm] = useState<ProfileForm>(emptyForm);
  const [loadedIdentity, setLoadedIdentity] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  // Held in local state rather than bound to the query, so a background refetch cannot discard what
  // somebody is halfway through typing - and adjusted during render rather than in an effect, which
  // is the pattern React documents for "reset state when the thing being edited changes". An effect
  // would render the stale form once first, and cascade a second render on every arrival.
  if (profile && loadedIdentity !== identityOf(profile)) {
    setLoadedIdentity(identityOf(profile));
    setForm(toForm(profile));
  }

  const set = (field: keyof ProfileForm) => (value: string) => {
    setForm((current) => ({ ...current, [field]: value }));
    setSaved(false);
  };

  const submit = () => {
    setSaved(false);
    update.mutate(
      {
        legalName: form.legalName.trim(),
        displayName: form.displayName.trim() || null,
        billingContactName: form.billingContactName.trim(),
        billingContactEmail: form.billingContactEmail.trim(),
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
        organizationId,
      },
      { onSuccess: () => setSaved(true) },
    );
  };

  const fieldErrors = update.error instanceof Error ? update.error.message : null;
  const missing = profile ? describeMissingProfileFields(profile.missingFields) : "";

  return (
    <div className="flex flex-col gap-6">
      <SubscriptionPlanPageHeader
        title="Billing profile"
        description="The name, contact and address every invoice and credit note for this organization is addressed to."
        backTo="/dashboard/subscription/plans"
        icon={<ReceiptText className="h-6 w-6" />}
      />

      {profile && !profile.isComplete && (
        <Card
          className="flex items-start gap-3 border-amber-300 bg-amber-50 p-4"
          data-testid="profile-incomplete"
        >
          <AlertCircle className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" />
          <div className="text-sm">
            <p className="font-medium">This profile is not complete yet.</p>
            <p className="text-muted-foreground">{missing}</p>
          </div>
        </Card>
      )}

      {profile?.isComplete && (
        <Card
          className="flex items-start gap-3 border-emerald-300 bg-emerald-50 p-4"
          data-testid="profile-complete"
        >
          <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-emerald-600" />
          <div className="text-sm">
            <p className="font-medium">Ready to be invoiced.</p>
            <p className="text-muted-foreground">
              Changes here appear on documents issued from now on. Invoices already sent keep the
              details they were issued with.
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
            <Label htmlFor="legalName">Legal name</Label>
            <Input
              id="legalName"
              value={form.legalName}
              onChange={(event) => set("legalName")(event.target.value)}
              placeholder="Northwind Trading AG"
            />
            <p className="text-xs text-muted-foreground">
              The name the organization contracts under. This is what a document has to carry.
            </p>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="displayName">Display name</Label>
            <Input
              id="displayName"
              value={form.displayName}
              onChange={(event) => set("displayName")(event.target.value)}
              placeholder="Northwind"
            />
            <p className="text-xs text-muted-foreground">
              Optional. Falls back to the legal name.
            </p>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="billingContactName">Billing contact</Label>
            <Input
              id="billingContactName"
              value={form.billingContactName}
              onChange={(event) => set("billingContactName")(event.target.value)}
              placeholder="Ada Byron"
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="billingContactEmail">Billing email</Label>
            <Input
              id="billingContactEmail"
              type="email"
              value={form.billingContactEmail}
              onChange={(event) => set("billingContactEmail")(event.target.value)}
              placeholder="billing@northwind.example"
            />
            <p className="text-xs text-muted-foreground">
              Where invoices, trial invoices and credit notes are emailed.
            </p>
          </div>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <div className="flex flex-col gap-2 sm:col-span-2">
            <Label htmlFor="line1">Address</Label>
            <Input
              id="line1"
              value={form.line1}
              onChange={(event) => set("line1")(event.target.value)}
              placeholder="1 Bahnhofstrasse"
            />
            <Input
              id="line2"
              value={form.line2}
              onChange={(event) => set("line2")(event.target.value)}
              placeholder="Second line, if there is one"
            />
            <p className="text-xs text-muted-foreground">
              Optional — many subscribers have no address to state, and a subscription is never
              refused for the want of one.
            </p>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="postalCode">Postal code</Label>
            <Input
              id="postalCode"
              value={form.postalCode}
              onChange={(event) => set("postalCode")(event.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="city">City</Label>
            <Input
              id="city"
              value={form.city}
              onChange={(event) => set("city")(event.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="region">State or region</Label>
            <Input
              id="region"
              value={form.region}
              onChange={(event) => set("region")(event.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="countryCode">Country code</Label>
            <Input
              id="countryCode"
              value={form.countryCode}
              onChange={(event) => set("countryCode")(event.target.value)}
              maxLength={2}
              placeholder="CH"
            />
            <p className="text-xs text-muted-foreground">Two letters, ISO 3166-1.</p>
          </div>

          <div className="flex flex-col gap-2 sm:col-span-2">
            <Label htmlFor="taxRegistrationId">Tax or VAT registration</Label>
            <Input
              id="taxRegistrationId"
              value={form.taxRegistrationId}
              onChange={(event) => set("taxRegistrationId")(event.target.value)}
              placeholder="CHE-123.456.789"
            />
            <p className="text-xs text-muted-foreground">
              Printed exactly as entered. Every jurisdiction spells these differently, so nothing
              here reformats it.
            </p>
          </div>
        </div>

        {fieldErrors && (
          <p className="text-sm text-destructive" data-testid="profile-error">
            {fieldErrors}
          </p>
        )}

        {saved && (
          <p className="text-sm text-emerald-700" data-testid="profile-saved">
            Saved. Documents issued from now on carry these details.
          </p>
        )}

        <div className="flex items-center gap-3">
          <Button onClick={submit} disabled={isLoading || update.isPending}>
            {update.isPending ? "Saving…" : "Save billing profile"}
          </Button>
          <span className="text-xs text-muted-foreground">
            Editing this never changes an invoice that has already been issued.
          </span>
        </div>
      </Card>
    </div>
  );
};
