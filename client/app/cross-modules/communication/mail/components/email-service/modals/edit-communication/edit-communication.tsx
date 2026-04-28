

import { Button } from "@/components/ui-kits/button/button";
import { CardContent } from "@/components/ui-kits/card/card";
import {
  DialogContent,
  DialogDescription,
  DialogFooter,
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { showErrorToast, toast } from "@/hooks/use-toast";
import { useProjectStore } from "@/store/useProjectStore";
import { useGetEmailConfigs } from "@blocks-communication/mail/hooks/use-email-config";
import { IEmailTemplate } from "@blocks-communication/mail/models/email";
import { useGetLanguages } from "@blocks-localization/hooks/use-language-manager";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { isErrorWithErrors } from "@/lib/error";
import { useSaveMailTemplate } from "../../../../hooks/use-email-template";

interface EditCommunicationProps {
  dialogTitle: string;
  templateData: IEmailTemplate;
  onClose: () => void;
}

const EditCommunication = (props: EditCommunicationProps) => {
  const { isLoading, data } = useGetEmailConfigs(0, 100);
  // const { saveEmailTemplate, isPending } = useSaveEmailTemplate();
  const { isPending: isSaveTemplateLoading, mutateAsync: saveTemplate } = useSaveMailTemplate();
  const { isLoading: isLanguageListLoading, data: languageListData } = useGetLanguages();
  const schema = z.object({
    mailConfigurationId: z.string().min(1, { message: "MailConfiguration is required" }),
    language: z.string().min(1, { message: "Language is required" }),
    name: z
      .string()
      .min(1, { message: "Name is required" })
      .max(50, { message: "Name must be less than 50 characters" })
      .regex(/^[^\s-]+$/, { message: "Name cannot contain spaces or hyphens" }),
    templateSubject: z
      .string()
      .min(1, { message: "Subject is required" })
      .max(150, { message: "Subject must be less than 150 characters" }),
  });
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  const form = useForm<IEmailTemplate>({
    values: props.templateData
      ? {
          itemId: props.templateData.itemId,
          mailConfigurationId: props.templateData.mailConfigurationId,
          language: props.templateData.language,
          name: props.templateData.name,
          templateSubject: props.templateData.templateSubject,
          generatedBy: props.templateData.generatedBy,
        }
      : undefined,
    resolver: zodResolver(schema),
  });

  const formSubmitHandler = async (data: any) => {
    try {
      const payload = {
        ...data,
        itemId: props.templateData.itemId,
        projectKey: tenantId,
      };
      const res = await saveTemplate(payload);
      props.onClose();
      if (res?.isSuccess) {
        toast({
          variant: "success",
          title: "Success",
          description: "Template updated",
        });
      } else {
        showErrorToast({ errors: res.errors });
      }
    } catch (error) {
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors });
      } else {
        showErrorToast({ errors: "Something went wrong" });
      }
    }
  };
  return (
    <DialogContent className="rounded-md sm:max-w-[700px]">
      <DialogHeader>
        <DialogTitle className="text-left">{props.dialogTitle}</DialogTitle>
      </DialogHeader>
      {data && !isLoading && !isLanguageListLoading && (
        <Form {...form}>
          {" "}
          <form onSubmit={form.handleSubmit(formSubmitHandler)}>
            <DialogDescription className="flex flex-col gap-6 pb-5">
              <CardContent>
                <div className="mb-4 grid gap-2">
                  <FormField
                    name="templateSubject"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel className="text-left font-medium text-high-emphasis">
                          {" "}
                          Subject
                        </FormLabel>
                        <FormControl>
                          <Input
                            placeholder="Enter subject"
                            className="border-default col-span-3 mt-1 border shadow-none"
                            {...field}
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </div>
                <div className="grid gap-4 sm:grid-cols-1">
                  <div className="grid gap-2">
                    <FormField
                      name="name"
                      control={form.control}
                      render={({ field }) => (
                        <FormItem>
                          <FormLabel className="text-left font-medium text-high-emphasis">
                            {" "}
                            Template name
                          </FormLabel>
                          <FormControl>
                            <Input
                              placeholder="Enter Template name"
                              className="border-default col-span-3 mt-1 border shadow-none"
                              {...field}
                              disabled={props.templateData.generatedBy === "Tenant"}
                              onKeyDown={(e) => {
                                if (e.key === " " || e.key === "_") {
                                  e.preventDefault();
                                }
                              }}
                            />
                          </FormControl>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                  </div>
                </div>
                <div className="mt-4 grid gap-4 sm:grid-cols-2">
                  <div className="grid gap-2">
                    <FormField
                      control={form.control}
                      name="language"
                      render={({ field }) => (
                        <FormItem>
                          <FormLabel className="text-left font-medium text-high-emphasis">
                            Language
                          </FormLabel>
                          <Select onValueChange={field.onChange} defaultValue={field.value}>
                            <FormControl>
                              <SelectTrigger className="border-default col-span-3 flex h-10 w-full items-center justify-between rounded-md border bg-background px-3 py-2 text-sm shadow-none placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2">
                                <SelectValue placeholder="Select language" />
                              </SelectTrigger>
                            </FormControl>
                            <SelectContent>
                              {(languageListData ?? []).map((language) => (
                                <SelectItem
                                  key={language.languageCode}
                                  value={language.languageCode}
                                >
                                  {language.languageName}
                                </SelectItem>
                              ))}
                            </SelectContent>
                          </Select>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                  </div>
                  <div className="grid gap-2">
                    <FormField
                      control={form.control}
                      name="mailConfigurationId"
                      render={({ field }) => (
                        <FormItem>
                          <FormLabel className="text-left font-medium text-high-emphasis">
                            Email Configuration
                          </FormLabel>
                          <Select onValueChange={field.onChange} defaultValue={field.value}>
                            <FormControl>
                              <SelectTrigger className="border-default col-span-3 flex h-10 w-full items-center justify-between rounded-md border bg-background px-3 py-2 text-sm shadow-none placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2">
                                <SelectValue placeholder="Select Configuration" />
                              </SelectTrigger>
                            </FormControl>
                            <SelectContent>
                              {data.map((config) => (
                                <SelectItem key={config.itemId} value={config.itemId}>
                                  {config.name}
                                </SelectItem>
                              ))}
                            </SelectContent>
                          </Select>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                  </div>
                </div>
              </CardContent>
            </DialogDescription>
            <DialogFooter className="flex flex-row gap-2">
              <DialogTrigger asChild>
                <Button disabled={isSaveTemplateLoading} variant="secondary">
                  Cancel
                </Button>
              </DialogTrigger>
              <Button>Save</Button>
            </DialogFooter>
          </form>
        </Form>
      )}
    </DialogContent>
  );
};

export default EditCommunication;
