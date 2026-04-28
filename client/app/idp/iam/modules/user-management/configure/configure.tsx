
import { Edit, Undo2 } from "lucide-react";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Input } from "@/components/ui-kits/input/input";
import { Button } from "@/components/ui-kits/button/button";
import { useForm } from "react-hook-form";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { iamConfigFormDefaultValues, iamConfigFormSchema } from "./utils";
import { zodResolver } from "@hookform/resolvers/zod";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useProjectStore } from "@/store/useProjectStore";
import {
  useGetIamConfiguration,
  useSaveIamConfiguration,
} from "@blocks-idp/iam/hooks/use-iam-configuration";
import { IIAMConfiguration } from "@blocks-idp/iam/models/configuration.model";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { isErrorWithErrors } from "@/lib/error";

export function Configure() {
  const { isLoading, data } = useGetIamConfiguration();
  const { mutateAsync, isPending } = useSaveIamConfiguration();
  const tenantId = useProjectStore().selectedProject?.tenantId || "";

  const form = useForm<IIAMConfiguration>({
    defaultValues: iamConfigFormDefaultValues,
    values: data?.data,
    resolver: zodResolver(iamConfigFormSchema),
  });

  const submitHandler = async (data: IIAMConfiguration) => {
    try {
      await mutateAsync({
        ...data,
        projectKey: tenantId,
      });
      form.reset(data);
      showSuccessToast({ description: "Configuration updated successfully" });
    } catch (error) {
      if (isErrorWithErrors(error)) {
        const { errors } = error;
        showErrorToast({ errors });
      }
    }
  };

  return (
    <main className="px-4 pt-4 md:px-6 md:pt-6">
      <PageBreadcrumb breadcrumbIndex={2} />

      <div className="mb-6 mt-2 flex h-8 items-center justify-between">
        <div className="item-center flex gap-2">
          <h1 className="text-2xl font-semibold">User Configuration</h1>
        </div>
      </div>
      <Card>
        <CardContent>
          {isLoading ? (
            <div className="flex flex-col gap-3 md:grid md:grid-cols-2 md:gap-4 lg:gap-6">
              {[1, 1, 1, 1, 1].map((_, index) => (
                <div key={index}>
                  <Skeleton className="h-5 w-1/2" />
                  <Skeleton className="mt-2 h-10 w-full" />
                </div>
              ))}
            </div>
          ) : (
            <Form {...form}>
              <form onSubmit={form.handleSubmit(submitHandler)} onReset={() => form.reset()}>
                <div className="flex flex-col gap-4 md:grid md:grid-cols-2 md:gap-4 lg:gap-6">
                  <FormField
                    name="accountActivationUrl"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel className="text-muted-foreground">
                          Account activation url
                        </FormLabel>
                        <FormControl>
                          <Input placeholder="Enter account activation url" {...field} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    name="accountVerificationUrl"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel className="text-muted-foreground">
                          Account verification url
                        </FormLabel>
                        <FormControl>
                          <Input placeholder="Enter account verification url" {...field} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    name="recoverAccountUrl"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel className="text-muted-foreground">
                          Recovery account URL
                        </FormLabel>
                        <FormControl>
                          <Input placeholder="Enter recovery account URL" {...field} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />

                  <FormField
                    name="activationUrlLifetimeInMinutes"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel className="text-muted-foreground">
                          Activation url lifetime (mins)
                        </FormLabel>
                        <FormControl>
                          <Input
                            type="number"
                            placeholder="Enter activation url lifetime (mins)"
                            {...field}
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    name="recoverAccountUrlLifetimeInMinutes"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel className="text-muted-foreground">
                          Recovery account URL lifetime (mins)
                        </FormLabel>
                        <FormControl>
                          <Input
                            type="number"
                            placeholder="Enter recovery account URL lifetime (mins)"
                            {...field}
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    name="logoutOnPasswordChange"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem className="mt-4 flex items-center gap-2 md:mt-8 md:h-fit md:self-center">
                        <FormControl>
                          <Checkbox
                            className="h-5 w-5"
                            checked={field.value}
                            onCheckedChange={field.onChange}
                          />
                        </FormControl>
                        <FormLabel className="!mt-0 text-muted-foreground">
                          Logout on password change
                        </FormLabel>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </div>

                <div className="mt-6 flex items-center justify-end gap-2">
                  <Button variant="outline" size="sm" className="h-10" type="reset">
                    <Undo2 className="mr-2 h-5 w-4" />
                    Reset
                  </Button>
                  <Button
                    size="sm"
                    variant="default"
                    className="h-10 text-primary-foreground"
                    disabled={isPending || !form.formState.isDirty}
                  >
                    <Edit className="mr-2 h-5 w-4" />

                    {isPending ? "Changing" : "Change"}
                  </Button>
                </div>
              </form>
            </Form>
          )}
        </CardContent>
      </Card>
    </main>
  );
}
