import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { useProjectStore } from "@/store/useProjectStore";
import { authGrantTypeFormDefaultValues, authGrantTypeFormSchema, authGrantTypeFormType } from "./utils";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Form, FormControl, FormField, FormItem, FormLabel } from "@/components/ui-kits/form/form";
import { GRANT_TYPES_OPTIONS } from "@blocks-idp/authentication/constants/authentication.constant";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useGetAuthConfig, useSaveAuthConfig } from "@blocks-idp/authentication/hooks/use-auth-config";

export const GrantTypes = () => {
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "", itemId: "" };
  const { data, isLoading } = useGetAuthConfig({ projectKey: tenantId });

  const { mutateAsync, isPending } = useSaveAuthConfig({ projectKey: tenantId });

  const form = useForm<authGrantTypeFormType>({
    defaultValues: authGrantTypeFormDefaultValues,
    values: data,
    resolver: zodResolver(authGrantTypeFormSchema),
  });

  const submitHandler = async (values: authGrantTypeFormType) => {
    try {
      if (!tenantId || !data) return showErrorToast({ errors: "Something went wrong" });

      const res = await mutateAsync({
        ...data,
        ...values,
        projectKey: tenantId,
      });

      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({ description: "Grant types updated successfully" });
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: "Something went wrong" });
    }
  };
  const { isValid, isDirty } = form.formState;
  return (
    <Card>
      <CardHeader>
        <CardTitle>Grant Types</CardTitle>
      </CardHeader>
      <CardContent>
        <Form {...form}>
          <form onSubmit={form.handleSubmit(submitHandler)} className="grid grid-cols-1 gap-4">
            {GRANT_TYPES_OPTIONS.map((item) => (
              <FormField
                key={item.id}
                control={form.control}
                name="allowedGrantTypes"
                render={({ field }) => (
                  <>
                    {isLoading ? (
                      <div className="flex items-center gap-2">
                        <Skeleton className="aspect-square w-5" />
                        <Skeleton className="h-5 w-32" />
                      </div>
                    ) : (
                      <FormItem className="flex items-center gap-2">
                        <FormControl>
                          <Checkbox
                            {...field}
                            checked={field.value?.includes(item.value)}
                            onCheckedChange={(checked) => {
                              if (checked) return field.onChange([...field.value, item.value]);
                              field.value.splice(field.value.indexOf(item.value), 1);
                              field.onChange([...field.value]);
                            }}
                          />
                        </FormControl>
                        <FormLabel className={`!mt-0`}>{item.label}</FormLabel>
                      </FormItem>
                    )}
                  </>
                )}
              />
            ))}
            {!isLoading && (
              <div>
                <Button disabled={isPending || !isValid || !isDirty}>Save</Button>
              </div>
            )}
          </form>
        </Form>
      </CardContent>
    </Card>
  );
};
