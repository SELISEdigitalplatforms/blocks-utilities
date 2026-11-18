import { ChevronDown } from "lucide-react";
import { useFormContext } from "react-hook-form";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore, useT } from "@seliseblocks/genesis-os";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui-kits/collapsible/collapsible";
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Input } from "@/components/ui-kits/input/input";
import { RadioGroup, RadioGroupItem } from "@/components/ui-kits/radio-group/radio-group";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import {
  ORGANIZATION_PAGE_SIZE,
  TENANT_WIDE_ORGANIZATION,
} from "../../constants/subscription.constants";
import type { CreateSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";
import { StepHeading } from "./step-heading";

/**
 * @param isEditing
 * Locks the code and the organization. Neither may move once a plan exists: a code is what
 * configuration points at, and a scope change would take the plan out from under whoever can see
 * it. The server ignores both on an edit; showing them read-only says so rather than letting
 * someone type a change that silently does nothing.
 */
const ScopeOption = ({ value, label, detail }: { value: string; label: string; detail: string }) => (
  <label
    htmlFor={`subscriber-scope-${value}`}
    className="flex cursor-pointer items-start gap-3 rounded-lg border p-3 has-[:disabled]:cursor-not-allowed has-[[data-state=checked]]:border-blocks-primary-400"
  >
    <RadioGroupItem value={value} id={`subscriber-scope-${value}`} className="mt-0.5" />
    <span className="space-y-0.5">
      <span className="block text-sm font-medium">{label}</span>
      <span className="block text-xs text-muted-foreground">{detail}</span>
    </span>
  </label>
);

export const StepIdentity = ({ isEditing = false }: { isEditing?: boolean }) => {
  const { t } = useT("utilities.subscription.plan-builder");
  const { control } = useFormContext<CreateSubscriptionPlanFormValues>();
  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const { data: organizationsData, isError: organizationsFailed } = useGetOrganizations({
    projectKey: tenantId,
    page: 0,
    pageSize: ORGANIZATION_PAGE_SIZE,
  });
  const organizations = organizationsData?.organizations ?? [];

  return (
    <div className="space-y-6">
      <StepHeading
        eyebrow={t("STEP_IDENTITY.TITLE")}
        title={t("STEP_IDENTITY.TITLE")}
        description={t("STEP_IDENTITY.DESCRIPTION")}
      />

      <div className="grid gap-5 sm:grid-cols-2">
        <FormField
          control={control}
          name="displayName"
          render={({ field }) => (
            <FormItem>
              <FormLabel>{t("STEP_IDENTITY.DISPLAY_NAME")}</FormLabel>
              <FormControl>
                <Input {...field} placeholder={t("STEP_IDENTITY.DISPLAY_NAME_PLACEHOLDER")} />
              </FormControl>
              <FormDescription>{t("STEP_IDENTITY.DISPLAY_NAME_HINT")}</FormDescription>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={control}
          name="code"
          render={({ field }) => (
            <FormItem>
              <FormLabel>{t("STEP_IDENTITY.CODE")}</FormLabel>
              <FormControl>
                <Input
                  {...field}
                  // eslint-disable-next-line no-restricted-syntax -- example value shown as a placeholder, not prose
                  placeholder="pro"
                  autoComplete="off"
                  spellCheck={false}
                  disabled={isEditing}
                />
              </FormControl>
              <FormDescription>
                {isEditing
                  ? t("STEP_IDENTITY.CODE_HINT_FIXED")
                  : t("STEP_IDENTITY.CODE_HINT")}
              </FormDescription>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={control}
          name="organizationId"
          render={({ field }) => (
            <FormItem>
              <FormLabel>{t("STEP_IDENTITY.ORGANIZATION")}</FormLabel>
              <Select
                value={field.value}
                onValueChange={field.onChange}
                disabled={isEditing}
              >
                <FormControl>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                </FormControl>
                <SelectContent>
                  <SelectItem value={TENANT_WIDE_ORGANIZATION}>
                    {t("STEP_IDENTITY.TENANT_WIDE")}
                  </SelectItem>
                  {organizations.map((organization) => (
                    <SelectItem key={organization.itemId} value={organization.itemId}>
                      {organization.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <FormDescription>
                {isEditing
                  ? t("STEP_IDENTITY.ORGANIZATION_HINT_FIXED")
                  : organizationsFailed
                    ? t("STEP_IDENTITY.ORGANIZATION_HINT_FAILED")
                    : t("STEP_IDENTITY.ORGANIZATION_HINT")}
              </FormDescription>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={control}
          name="subscriberScope"
          render={({ field }) => (
            <FormItem className="sm:col-span-2">
              <FormLabel>{t("STEP_IDENTITY.SCOPE")}</FormLabel>
              <FormControl>
                <RadioGroup
                  value={field.value}
                  onValueChange={field.onChange}
                  disabled={isEditing}
                  className="gap-3 sm:grid-cols-2"
                >
                  <ScopeOption
                    value="Organization"
                    label={t("STEP_IDENTITY.SCOPE_ORGANIZATION")}
                    detail={t("STEP_IDENTITY.SCOPE_ORGANIZATION_DETAIL")}
                  />
                  <ScopeOption
                    value="User"
                    label={t("STEP_IDENTITY.SCOPE_USER")}
                    detail={t("STEP_IDENTITY.SCOPE_USER_DETAIL")}
                  />
                </RadioGroup>
              </FormControl>
              <FormDescription>
                {isEditing
                  ? t("STEP_IDENTITY.SCOPE_HINT_FIXED")
                  : t("STEP_IDENTITY.SCOPE_HINT")}
              </FormDescription>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={control}
          name="description"
          render={({ field }) => (
            <FormItem className="sm:col-span-2">
              <FormLabel>{t("common:DESCRIPTION")}</FormLabel>
              <FormControl>
                <Textarea {...field} placeholder={t("STEP_IDENTITY.DESCRIPTION_PLACEHOLDER")} rows={2} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={control}
          name="familyCode"
          render={({ field }) => (
            <FormItem>
              <FormLabel>{t("STEP_IDENTITY.FAMILY_CODE")}</FormLabel>
              {/* eslint-disable-next-line no-restricted-syntax -- example value shown as a placeholder, not prose */}
              <FormControl><Input {...field} placeholder="claude-max" /></FormControl>
              <FormDescription>{t("STEP_IDENTITY.FAMILY_CODE_HINT")}</FormDescription>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={control}
          name="familyRank"
          render={({ field }) => (
            <FormItem>
              <FormLabel>{t("STEP_IDENTITY.FAMILY_RANK")}</FormLabel>
              <FormControl><Input {...field} type="number" min={0} placeholder="10" /></FormControl>
              <FormDescription>{t("STEP_IDENTITY.FAMILY_RANK_HINT")}</FormDescription>
              <FormMessage />
            </FormItem>
          )}
        />
      </div>

      <Collapsible>
        <CollapsibleTrigger className="flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ChevronDown className="h-4 w-4" />
          {t("STEP_IDENTITY.FEATURES_TRIGGER")}
        </CollapsibleTrigger>
        <CollapsibleContent className="pt-3">
          <FormField
            control={control}
            name="featuresJson"
            render={({ field }) => (
              <FormItem>
                <FormControl>
                  <Textarea
                    {...field}
                    // eslint-disable-next-line no-restricted-syntax -- example value shown as a placeholder, not prose
                    placeholder='{"betaAccess": true}'
                    rows={4}
                    spellCheck={false}
                    className="font-mono text-xs"
                  />
                </FormControl>
                <FormDescription>
                  {t("STEP_IDENTITY.FEATURES_HINT")}
                </FormDescription>
                <FormMessage />
              </FormItem>
            )}
          />
        </CollapsibleContent>
      </Collapsible>
    </div>
  );
};
