import { useProjectStore, useT } from "@seliseblocks/genesis-os";
import { useState } from "react";
import { AlertCircle, ArrowRight, Building2, CheckCircle2, ReceiptText } from "lucide-react";
import { Link, useSearchParams } from "react-router";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { RichText } from "../components/rich-text";
import { ORGANIZATION_PAGE_SIZE, ORGANIZATION_QUERY_PARAM } from "../constants/subscription.constants";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import { useBillingProfile, useUpdateBillingProfile } from "../hooks/use-billing-profile";
import { useOrganizationScope } from "../hooks/use-organization-scope";
import { useSubscriptionLink } from "../hooks/use-subscription-link";
import type { SubscriptionBillingProfile } from "../models/subscription-billing.model";
import { describeMissingProfileFields } from "../utilities/financial-document-format";

/**
 * Suggestions only. The tag has to match a language the tenant has written its email templates in,
 * and the mail module has no fallback between languages, so any well-formed tag is accepted.
 */
const commonLanguageTags = ["en-US", "en-GB", "de-CH", "de-DE", "fr-CH", "fr-FR", "it-CH"];

/** The picker's value for "no organization named": the caller's own, the console's for an admin. */
const OWN_ORGANIZATION = "__own__";

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
  billingContactLanguage: string;
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
  billingContactLanguage: "",
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
  billingContactLanguage: profile.billingContactLanguage ?? "",
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
  const { t } = useT("utilities.subscription.pages");
  const organizationId = useOrganizationScope();
  const [searchParams, setSearchParams] = useSearchParams();
  const subscriptionLink = useSubscriptionLink();
  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const { data: organizationsData } = useGetOrganizations({
    projectKey: tenantId,
    page: 0,
    pageSize: ORGANIZATION_PAGE_SIZE,
  });
  const organizations = organizationsData?.organizations ?? [];
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

  // Found on dev: from the menu this page only ever edited the console's own profile, so a profile
  // "completed" for a customer was in fact the console's, and the customer's activation was refused.
  // The scope stays in the URL, so links that already name an organization keep working.
  const chooseOrganization = (value: string) => {
    const next = new URLSearchParams(searchParams);
    if (value === OWN_ORGANIZATION) {
      next.delete(ORGANIZATION_QUERY_PARAM);
    } else {
      next.set(ORGANIZATION_QUERY_PARAM, value);
    }
    setSearchParams(next, { replace: true });
    setSaved(false);
  };

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
        billingContactLanguage: form.billingContactLanguage.trim() || null,
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

  // The organization the server actually answered about, which is not always the one the URL asked
  // for: naming another organization is honoured for the platform console alone, and for everybody
  // else the server quietly resolves the caller's own. Taken from the response rather than the URL,
  // so the page states what it loaded instead of what it hoped for.
  const resolvedOrganizationId = profile?.organizationId;
  const scopeHonoured =
    !organizationId || !resolvedOrganizationId || resolvedOrganizationId === organizationId;

  // The profile's own name, which is what the invoices will be addressed to. Blank until somebody
  // fills it in, and a blank heading is better than one naming the organization it is not.
  const subscriberName = (profile?.displayName || profile?.legalName || "").trim();

  return (
    <div className="flex flex-col gap-6">
      <SubscriptionPlanPageHeader
        title={t("BILLING_PROFILE_PAGE.TITLE")}
        description={t("BILLING_PROFILE_PAGE.DESCRIPTION")}
        backTo={subscriptionLink("plans")}
        icon={<ReceiptText className="h-6 w-6" />}
      />

      {organizations.length > 0 && (
        <div className="flex flex-col gap-2 sm:w-80">
          <Label htmlFor="billingProfileOrganization">{t("BILLING_PROFILE_PAGE.ORGANIZATION")}</Label>
          <Select value={organizationId ?? OWN_ORGANIZATION} onValueChange={chooseOrganization}>
            <SelectTrigger id="billingProfileOrganization" aria-label={t("BILLING_PROFILE_PAGE.ORGANIZATION")}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={OWN_ORGANIZATION}>{t("BILLING_PROFILE_PAGE.OWN_ORGANIZATION")}</SelectItem>
              {organizations.map((organization) => (
                <SelectItem key={organization.itemId} value={organization.itemId}>
                  {organization.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      )}

      {resolvedOrganizationId && (
        <Card
          className="flex items-start gap-3 border-blocks-primary-200 bg-blocks-primary-shades-100 p-4"
          data-testid="profile-scope"
        >
          <Building2 className="mt-0.5 h-5 w-5 shrink-0 text-blocks-primary-600" />
          <div className="text-sm">
            <p className="font-medium">
              {subscriberName
                ? t("BILLING_PROFILE_PAGE.SCOPE_FOR_NAME", { name: subscriberName })
                : t("BILLING_PROFILE_PAGE.SCOPE_FOR_ORGANIZATION")}{" "}
              — <span className="font-mono text-xs">{resolvedOrganizationId}</span>
            </p>
            <p className="text-muted-foreground">
              {t("BILLING_PROFILE_PAGE.SCOPE_HINT")}
            </p>
          </div>
        </Card>
      )}

      {!scopeHonoured && (
        <Card
          className="flex items-start gap-3 border-amber-300 bg-amber-50 p-4"
          data-testid="profile-scope-mismatch"
        >
          <AlertCircle className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" />
          <div className="text-sm">
            <p className="font-medium">{t("BILLING_PROFILE_PAGE.MISMATCH_TITLE")}</p>
            <p className="text-muted-foreground">
              <RichText
                text={t("BILLING_PROFILE_PAGE.MISMATCH_DESCRIPTION", {
                  requested: `<code>${organizationId}</code>`,
                  resolved: `<code>${resolvedOrganizationId}</code>`,
                })}
                codeClassName="font-mono text-xs"
              />
            </p>
          </div>
        </Card>
      )}

      {profile && scopeHonoured && !profile.isComplete && (
        <Card
          className="flex items-start gap-3 border-amber-300 bg-amber-50 p-4"
          data-testid="profile-incomplete"
        >
          <AlertCircle className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" />
          <div className="text-sm">
            <p className="font-medium">{t("BILLING_PROFILE_PAGE.INCOMPLETE")}</p>
            <p className="text-muted-foreground">{missing}</p>
          </div>
        </Card>
      )}

      {profile?.isComplete && scopeHonoured && (
        <Card
          className="flex items-start gap-3 border-emerald-300 bg-emerald-50 p-4"
          data-testid="profile-complete"
        >
          <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-emerald-600" />
          <div className="text-sm">
            <p className="font-medium">{t("BILLING_PROFILE_PAGE.COMPLETE")}</p>
            <p className="text-muted-foreground">
              {t("BILLING_PROFILE_PAGE.COMPLETE_HINT")}
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
            <Label htmlFor="legalName">{t("BILLING_PROFILE_PAGE.LEGAL_NAME")}</Label>
            <Input
              id="legalName"
              value={form.legalName}
              onChange={(event) => set("legalName")(event.target.value)}
              // eslint-disable-next-line no-restricted-syntax -- example value shown as a placeholder, not prose
              placeholder="Northwind Trading AG"
            />
            <p className="text-xs text-muted-foreground">
              {t("BILLING_PROFILE_PAGE.LEGAL_NAME_HINT")}
            </p>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="displayName">{t("BILLING_PROFILE_PAGE.DISPLAY_NAME")}</Label>
            <Input
              id="displayName"
              value={form.displayName}
              onChange={(event) => set("displayName")(event.target.value)}
              // eslint-disable-next-line no-restricted-syntax -- example value shown as a placeholder, not prose
              placeholder="Northwind"
            />
            <p className="text-xs text-muted-foreground">
              {t("BILLING_PROFILE_PAGE.DISPLAY_NAME_HINT")}
            </p>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="billingContactName">{t("BILLING_PROFILE_PAGE.CONTACT")}</Label>
            <Input
              id="billingContactName"
              value={form.billingContactName}
              onChange={(event) => set("billingContactName")(event.target.value)}
              // eslint-disable-next-line no-restricted-syntax -- example value shown as a placeholder, not prose
              placeholder="Ada Byron"
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="billingContactEmail">{t("BILLING_PROFILE_PAGE.EMAIL")}</Label>
            <Input
              id="billingContactEmail"
              type="email"
              value={form.billingContactEmail}
              onChange={(event) => set("billingContactEmail")(event.target.value)}
              // eslint-disable-next-line no-restricted-syntax -- example value shown as a placeholder, not prose
              placeholder="billing@northwind.example"
            />
            <p className="text-xs text-muted-foreground">
              {t("BILLING_PROFILE_PAGE.EMAIL_HINT")}
            </p>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="billingContactLanguage">{t("BILLING_PROFILE_PAGE.EMAIL_LANGUAGE")}</Label>
            <Input
              id="billingContactLanguage"
              list="billingContactLanguageOptions"
              value={form.billingContactLanguage}
              onChange={(event) => set("billingContactLanguage")(event.target.value)}
              maxLength={35}
              // eslint-disable-next-line no-restricted-syntax -- example value shown as a placeholder, not prose
              placeholder="en-US"
            />
            <datalist id="billingContactLanguageOptions">
              {commonLanguageTags.map((tag) => (
                <option key={tag} value={tag} />
              ))}
            </datalist>
            <p className="text-xs text-muted-foreground">
              {t("BILLING_PROFILE_PAGE.EMAIL_LANGUAGE_HINT")}
            </p>
          </div>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <div className="flex flex-col gap-2 sm:col-span-2">
            <Label htmlFor="line1">{t("BILLING_PROFILE_PAGE.ADDRESS")}</Label>
            <Input
              id="line1"
              value={form.line1}
              onChange={(event) => set("line1")(event.target.value)}
              // eslint-disable-next-line no-restricted-syntax -- example value shown as a placeholder, not prose
              placeholder="1 Bahnhofstrasse"
            />
            <Input
              id="line2"
              value={form.line2}
              onChange={(event) => set("line2")(event.target.value)}
              placeholder={t("BILLING_PROFILE_PAGE.ADDRESS_LINE2_PLACEHOLDER")}
            />
            <p className="text-xs text-muted-foreground">
              {t("BILLING_PROFILE_PAGE.ADDRESS_HINT")}
            </p>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="postalCode">{t("BILLING_PROFILE_PAGE.POSTAL_CODE")}</Label>
            <Input
              id="postalCode"
              value={form.postalCode}
              onChange={(event) => set("postalCode")(event.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="city">{t("BILLING_PROFILE_PAGE.CITY")}</Label>
            <Input
              id="city"
              value={form.city}
              onChange={(event) => set("city")(event.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="region">{t("BILLING_PROFILE_PAGE.REGION")}</Label>
            <Input
              id="region"
              value={form.region}
              onChange={(event) => set("region")(event.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="countryCode">{t("BILLING_PROFILE_PAGE.COUNTRY_CODE")}</Label>
            <Input
              id="countryCode"
              value={form.countryCode}
              onChange={(event) => set("countryCode")(event.target.value)}
              maxLength={2}
              placeholder="CH"
            />
            <p className="text-xs text-muted-foreground">{t("BILLING_PROFILE_PAGE.COUNTRY_CODE_HINT")}</p>
          </div>

          <div className="flex flex-col gap-2 sm:col-span-2">
            <Label htmlFor="taxRegistrationId">{t("BILLING_PROFILE_PAGE.TAX_REGISTRATION")}</Label>
            <Input
              id="taxRegistrationId"
              value={form.taxRegistrationId}
              onChange={(event) => set("taxRegistrationId")(event.target.value)}
              placeholder="CHE-123.456.789"
            />
            <p className="text-xs text-muted-foreground">
              {t("BILLING_PROFILE_PAGE.TAX_REGISTRATION_HINT")}
            </p>
          </div>
        </div>

        {fieldErrors && (
          <p className="text-sm text-destructive" data-testid="profile-error">
            {fieldErrors}
          </p>
        )}

        {saved && (
          <div className="flex flex-wrap items-center gap-3" data-testid="profile-saved">
            <p className="text-sm text-emerald-700">
              {t("BILLING_PROFILE_PAGE.SAVED")}
            </p>
            {/* Back to the catalogue for the same organization, because the usual reason for being
                here is a subscription that was refused for the want of these details — and retrying
                it against a different organization would fail for the same reason again. */}
            <Button asChild variant="outline" size="sm">
              <Link to={subscriptionLink("plans")}>
                {t("BILLING_PROFILE_PAGE.CONTINUE")}
                <ArrowRight className="ml-2 h-4 w-4" />
              </Link>
            </Button>
          </div>
        )}

        <div className="flex items-center gap-3">
          <Button onClick={submit} disabled={isLoading || update.isPending}>
            {update.isPending ? t("BILLING_PROFILE_PAGE.SAVING") : t("BILLING_PROFILE_PAGE.SAVE")}
          </Button>
          <span className="text-xs text-muted-foreground">
            {t("BILLING_PROFILE_PAGE.EDIT_HINT")}
          </span>
        </div>
      </Card>
    </div>
  );
};
