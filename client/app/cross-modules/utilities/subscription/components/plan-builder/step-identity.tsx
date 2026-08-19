import { ChevronDown } from "lucide-react";
import { useFormContext } from "react-hook-form";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/genesis-os";
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

/**
 * @param isEditing
 * Locks the code and the organization. Neither may move once a plan exists: a code is what
 * configuration points at, and a scope change would take the plan out from under whoever can see
 * it. The server ignores both on an edit; showing them read-only says so rather than letting
 * someone type a change that silently does nothing.
 */
export const StepIdentity = ({ isEditing = false }: { isEditing?: boolean }) => {
  const { control } = useFormContext<CreateSubscriptionPlanFormValues>();
  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const { data: organizationsData, isError: organizationsFailed } = useGetOrganizations({
    projectKey: tenantId,
    page: 0,
    pageSize: ORGANIZATION_PAGE_SIZE,
  });
  const organizations = organizationsData?.organizations ?? [];

  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-lg font-semibold">Identity</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          The basics: what this plan is called, and who it&apos;s for.
        </p>
      </div>

      <div className="grid gap-5 sm:grid-cols-2">
        <FormField
          control={control}
          name="displayName"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Display name</FormLabel>
              <FormControl>
                <Input {...field} placeholder="Pro plan" />
              </FormControl>
              <FormDescription>Shown to subscribers wherever the plan appears.</FormDescription>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={control}
          name="code"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Code</FormLabel>
              <FormControl>
                <Input
                  {...field}
                  placeholder="pro"
                  autoComplete="off"
                  spellCheck={false}
                  disabled={isEditing}
                />
              </FormControl>
              <FormDescription>
                {isEditing
                  ? "Fixed once the plan exists — this is what your own systems call it by."
                  : "Shown to no one but your own systems — the stable id you'll call this plan by. Lowercase letters, digits, hyphens and underscores only."}
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
              <FormLabel>Organization</FormLabel>
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
                    Tenant-wide — sold to every organization
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
                  ? "Fixed once the plan exists — moving it would take the plan out from under whoever can see it."
                  : organizationsFailed
                    ? "Organizations could not be loaded; you can still create a tenant-wide plan."
                    : "Which organization this plan belongs to. Scope it to one organization only when it shouldn't be offered to everyone."}
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
              <FormLabel>Description</FormLabel>
              <FormControl>
                <Textarea {...field} placeholder="What this plan is for, in a sentence." rows={2} />
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
              <FormLabel>Family code (optional)</FormLabel>
              <FormControl><Input {...field} placeholder="claude-max" /></FormControl>
              <FormDescription>Plans with the same family code are levels of one product.</FormDescription>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={control}
          name="familyRank"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Family rank</FormLabel>
              <FormControl><Input {...field} type="number" min={0} placeholder="10" /></FormControl>
              <FormDescription>Lower levels sort first; required when family code is set.</FormDescription>
              <FormMessage />
            </FormItem>
          )}
        />
      </div>

      <Collapsible>
        <CollapsibleTrigger className="flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ChevronDown className="h-4 w-4" />
          Advanced: raw features JSON
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
                    placeholder='{"betaAccess": true}'
                    rows={4}
                    spellCheck={false}
                    className="font-mono text-xs"
                  />
                </FormControl>
                <FormDescription>
                  Arbitrary feature flags, stored verbatim and handed back untouched — nothing here
                  interprets it. Leave blank unless your own product reads this field.
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
