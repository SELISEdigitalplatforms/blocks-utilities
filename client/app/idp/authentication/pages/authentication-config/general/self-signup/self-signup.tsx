import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { useProjectStore } from "@/store/useProjectStore";
import { selfSignUpFormDefaultValues, selfSignUpFormSchema, SelfSignUpFormType } from "./utils";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Form, FormControl, FormField, FormItem, FormLabel } from "@/components/ui-kits/form/form";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useGetAuthConfig, useSaveAuthConfig } from "@blocks-idp/authentication/hooks/use-auth-config";

export const SelfSignup = () => {
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "", itemId: "" };
  const { data, isLoading } = useGetAuthConfig({ projectKey: tenantId });

  const { mutateAsync, isPending } = useSaveAuthConfig({ projectKey: tenantId });

  const form = useForm<SelfSignUpFormType>({
    defaultValues: selfSignUpFormDefaultValues,
    values: data,
    resolver: zodResolver(selfSignUpFormSchema),
  });

  const submitHandler = async (values: SelfSignUpFormType) => {
    try {
      if (!tenantId || !data) return showErrorToast({ errors: "Something went wrong" });

      const res = await mutateAsync({
        ...data,
        ...values,
        projectKey: tenantId,
      });

      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({ description: "Self sign-up settings updated successfully" });
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: "Something went wrong" });
    }
  };

  const { isValid, isDirty } = form.formState;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Self Sign-Up</CardTitle>
      </CardHeader>
      <CardContent>
        <Form {...form}>
          <form onSubmit={form.handleSubmit(submitHandler)} className="grid grid-cols-1 gap-4">
            <FormField
              control={form.control}
              name="isSelfSignUpAllowed"
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
                          checked={field.value}
                          onCheckedChange={field.onChange}
                          value={undefined}
                        />
                      </FormControl>
                      <FormLabel className={`!mt-0`}>Allow Self Sign-Up</FormLabel>
                    </FormItem>
                  )}
                </>
              )}
            />
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
