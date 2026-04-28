

import React, { forwardRef, useImperativeHandle, useState } from "react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { useForm } from "react-hook-form";
import { IEmailTemplate } from "@blocks-communication/mail/models/email";
import { z } from "zod";
import { zodResolver } from "@hookform/resolvers/zod";
import { useGetEmailConfigs } from "@blocks-communication/mail/hooks/use-email-config";
import { useGetLanguages } from "@blocks-localization/hooks/use-language-manager";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
interface IBasicInformationProps {
  // eslint-disable-next-line no-unused-vars
  onSubmit(data: unknown): void;
  templateData: IEmailTemplate;
  onValidityChange?: (isValid: boolean) => void;
}
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
    .max(150, { message: "Subject must be less than 150 characters" })
    .refine((val) => val.trim().length > 0, {
      message: "Subject cannot contain only whitespace",
    }),
});
const BasicInformation = forwardRef(function Inner(
  { onSubmit, templateData, onValidityChange }: IBasicInformationProps,
  ref,
) {
  const { isLoading: isLanguageListLoading, data: languageListData } = useGetLanguages();
  const [filterData] = useState({ pageNumber: 0, pageSize: 10 });
  const { isLoading, data } = useGetEmailConfigs(filterData.pageNumber, filterData.pageSize);
  // const { getEmailConfigs, isPending } = useGetEmailConfigs();
  // const [mailConfigs, setData] = useState<IEmailConfig[]>([]);

  const form = useForm<IEmailTemplate>({
    defaultValues: {
      itemId: templateData.itemId,
      mailConfigurationId: templateData.mailConfigurationId,
      language: templateData.language,
      name: templateData.name,
      templateSubject: templateData.templateSubject,
      generatedBy: templateData.generatedBy,
    },
    resolver: zodResolver(schema),
    mode: "onChange",
    reValidateMode: "onChange",
  });

  // Notify parent of form validity changes
  React.useEffect(() => {
    onValidityChange?.(form.formState.isValid);
  }, [form.formState.isValid, onValidityChange]);

  useImperativeHandle(ref, () => {
    return {
      submit() {
        // console.log("submit");
        form.handleSubmit(onSubmit)();
      },
      isValid: form.formState.isValid,
    };
  }, [form.formState.isValid]);

  // useEffect(() => {
  //   const fetchData = async () => {
  //     try {
  //       const response = await getEmailConfigs();
  //       setData(response);
  //       console.log(mailConfigs);
  //     }
  //     catch (err) {
  //       console.error("Fetch error:", err);
  //       //setError(err instanceof Error ? err.message : "An error occurred while fetching data");
  //     }
  //     // finally {
  //     //   setLoading(false);
  //     // }
  //   };
  //   if (tenantId && tenantId !== "") {
  //     fetchData();
  //   }
  // }, [tenantId]);

  return (
    <main className="mt-[20%] text-left sm:mt-[10%]">
      <h3 className="mt-[-16px] text-3xl font-semibold tracking-tight">Basic Information</h3>
      {data && !isLoading && !isLanguageListLoading && (
        <Card className="mt-6 rounded-sm shadow-none">
          <Form {...form}>
            {" "}
            <form>
              <CardHeader>
                <CardTitle className="text-lg">About the Template</CardTitle>
              </CardHeader>
              <CardContent>
                <div className="grid gap-4 sm:grid-cols-2">
                  <div className="grid gap-2">
                    <FormField
                      name="name"
                      control={form.control}
                      render={({ field }) => (
                        <FormItem>
                          <FormLabel className="text-left font-medium text-high-emphasis">
                            {" "}
                            Name *
                          </FormLabel>
                          <FormControl>
                            <Input
                              placeholder="Enter name"
                              className="border-default col-span-3 mt-1 border shadow-none"
                              {...field}
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
                  <div className="grid gap-2">
                    {/* <Label htmlFor="id" className="text-left font-medium text-high-emphasis">
                    Email Configuration
                  </Label>
                  <Input
                    id="emailConfig"
                    placeholder="Select configuration"
                    className="border-default col-span-3 border shadow-none"
                  /> */}
                    <FormField
                      control={form.control}
                      name="mailConfigurationId"
                      render={({ field }) => (
                        <FormItem>
                          <FormLabel className="text-left font-medium text-high-emphasis">
                            Email Configuration *
                          </FormLabel>
                          <Select onValueChange={field.onChange} defaultValue={field.value}>
                            <FormControl>
                              <SelectTrigger className="border-default col-span-3 flex h-10 w-full items-center justify-between rounded-md border bg-background px-3 py-2 text-sm shadow-none placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2">
                                <SelectValue placeholder="Select Configuration" />
                              </SelectTrigger>
                            </FormControl>
                            <SelectContent>
                              {data
                                .filter((config) => !config.isInbound)
                                .map((config) => (
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
                <div className="mt-4 grid gap-4 sm:grid-cols-2">
                  <div className="grid gap-2">
                    {/* <Label htmlFor="language" className="text-left font-medium text-high-emphasis">
                    Language
                  </Label>
                  <Select>
                    <SelectTrigger className="border-default col-span-3 flex h-10 w-full items-center justify-between rounded-md border bg-background px-3 py-2 text-sm shadow-none placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2">
                      <SelectValue placeholder="Select language" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="english">English</SelectItem>
                      <SelectItem value="spanish">Spanish</SelectItem>
                      <SelectItem value="french">French</SelectItem>
                      <SelectItem value="german">German</SelectItem>
                    </SelectContent>
                  </Select> */}
                    <FormField
                      control={form.control}
                      name="language"
                      render={({ field }) => (
                        <FormItem>
                          <FormLabel className="text-left font-medium text-high-emphasis">
                            Language *
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
                </div>
                <div className="mt-4 grid gap-2">
                  {/* <Label htmlFor="subject">Subject</Label>
                <Input id="subject" placeholder="Enter subject" /> */}
                  <FormField
                    name="templateSubject"
                    control={form.control}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel className="text-left font-medium text-high-emphasis">
                          {" "}
                          Subject *
                        </FormLabel>
                        <FormControl>
                          <Input
                            placeholder="Enter subject"
                            className="border-default col-span-3 mt-1 border shadow-none"
                            {...field}
                            onBlur={(e) => {
                              field.onChange(e.target.value.trim());
                              field.onBlur();
                            }}
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </div>
              </CardContent>
              {/* <CardHeader className="mt-[-30px]">
              <CardTitle className="text-lg">Content</CardTitle>
            </CardHeader>
            <div
              className={`3xl:grid-cols-5 mx-6 mb-5 grid grid-cols-2 gap-8 text-center sm:gap-4 md:grid-cols-2 md:gap-8 lg:grid-cols-3 lg:gap-0 xl:grid-cols-5 xl:gap-40 2xl:grid-cols-7 2xl:gap-20`}
            >
              <Card className="w-30 h-30 rounded-sm shadow-none lg:h-40 lg:w-40">
                <div className="mt-4 flex flex-col items-center justify-center text-primary sm:mt-6">
                  <Plus size={useIsMobile() ? 32 : 64} strokeWidth={0.8} />
                  <h3 className="mt-4 text-sm sm:mt-6 sm:text-base">New Template</h3>
                </div>
              </Card>
              <Card className="w-30 h-30 rounded-sm shadow-none sm:ml-4 md:ml-0 lg:h-40 lg:w-40">
                <div className="mt-4 flex flex-col items-center justify-center text-primary sm:mt-6">
                  <LayoutTemplate size={useIsMobile() ? 32 : 64} strokeWidth={0.8} />
                  <h3 className="my-4 text-sm sm:mt-6 sm:text-base">Browse Templates</h3>
                </div>
              </Card>
            </div> */}
            </form>
          </Form>
        </Card>
      )}
    </main>
  );
});

export default BasicInformation;
