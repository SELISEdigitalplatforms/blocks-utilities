import { PrimaryButton } from "@/components/action-buttons/primary-button";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Input } from "@/components/ui-kits/input/input";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useProjectStore } from "@/store/useProjectStore";
import {
  useGetAuthConfig,
  useSaveAuthConfig,
} from "@blocks-idp/authentication/hooks/use-auth-config";
import { zodResolver } from "@hookform/resolvers/zod";
import { Pen } from "lucide-react";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { authConfigFormDefaultValues, authConfigFormSchema } from "./utils";

export const EditGeneralSettings = () => {
  const [open, setOpen] = useState<boolean>(false);
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const { data } = useGetAuthConfig({ projectKey: tenantId });

  const { mutateAsync, isPending } = useSaveAuthConfig({ projectKey: tenantId });

  const form = useForm<z.infer<typeof authConfigFormSchema>>({
    defaultValues: authConfigFormDefaultValues,
    resolver: zodResolver(authConfigFormSchema),
  });

  const {
    formState: { isDirty },
  } = form;

  const handleDialogOpenChange = (isOpen: boolean) => {
    if (isOpen) {
      form.reset(data || authConfigFormDefaultValues);
    }
    setOpen(isOpen);
  };

  const submitHandler = async (values: z.infer<typeof authConfigFormSchema>) => {
    try {
      if (!tenantId || !data) return showErrorToast({ errors: "Something went wrong" });

      const res = await mutateAsync({
        ...data,
        ...values,
        projectKey: tenantId,
      });

      if (!res.isSuccess) return showErrorToast({ errors: res.errors });

      showSuccessToast({ description: "Configuration updated successfully" });
      form.reset();
      setOpen(false);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: "Something went wrong" });
    }
  };
  return (
    <Dialog open={open} onOpenChange={handleDialogOpenChange}>
      <DialogTrigger asChild>
        <PrimaryButton label="Edit" Icon={Pen} />
      </DialogTrigger>
      <DialogContent className="max-w-3xl">
        <DialogHeader>
          <DialogTitle>Settings</DialogTitle>
        </DialogHeader>
        <Form {...form}>
          <form onSubmit={form.handleSubmit(submitHandler)} onReset={() => form.reset()}>
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <FormField
                name="accessTokenValidForNumberMinutes"
                control={form.control}
                render={({ field }) => (
                  <FormItem>
                    <FormLabel className="text-muted-foreground">
                      Access Token Validity (minutes)
                    </FormLabel>
                    <FormControl>
                      <Input type="number" placeholder="Set a duration in minutes" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                name="refreshTokenValidForNumberMinutes"
                control={form.control}
                render={({ field }) => (
                  <FormItem>
                    <FormLabel className="text-muted-foreground">
                      Refresh Token Validity (minutes)
                    </FormLabel>
                    <FormControl>
                      <Input type="number" placeholder="Set a duration in minutes" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                name="rememberMeRefreshTokenValidForNumberMinutes"
                control={form.control}
                render={({ field }) => (
                  <FormItem>
                    <FormLabel className="text-muted-foreground">
                      &#39;Remember Me&#39; Token Validity (minutes)
                    </FormLabel>
                    <FormControl>
                      <Input type="number" placeholder="Set a duration in minutes" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                name="getNumberOfWrongAttemptsToLockTheAccount"
                control={form.control}
                render={({ field }) => (
                  <FormItem>
                    <FormLabel className="text-muted-foreground">
                      Max Wrong Attempts Before Lock
                    </FormLabel>
                    <FormControl>
                      <Input type="number" placeholder="Enter number" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />

              <FormField
                name="accountLockDurationInMinutes"
                control={form.control}
                render={({ field }) => (
                  <FormItem className="">
                    <FormLabel className="text-muted-foreground">
                      Account Lock Duration (minutes)
                    </FormLabel>
                    <FormControl>
                      <Input type="number" placeholder="Set a duration in minutes" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </div>
            <div className="mt-5 flex items-center justify-end gap-4">
              <DialogClose>
                <Button variant="outline" className="h-9 w-20" size="sm" type="reset">
                  Cancel
                </Button>
              </DialogClose>
              <Button
                size="sm"
                variant="default"
                className="h-9 w-20 text-primary-foreground"
                disabled={isPending || !isDirty}
              >
                Save
              </Button>
            </div>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
