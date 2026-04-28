

import React, { useEffect, useState } from "react";
import { ArrowLeft, Pencil, Send } from "lucide-react";
import { CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { Dialog, DialogTrigger } from "@/components/ui-kits/dialog/dialog";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { IEmailTemplate } from "@blocks-communication/mail/models/email";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import EditCommunication from "@blocks-communication/mail/components/email-service/modals/edit-communication/edit-communication";
import { checkValidDate, formatFullDate, parseDateString } from "@/lib/utils";
import { toast } from "@/hooks/use-toast";
import { useGetUser } from "@blocks-idp/iam/hooks/use-user";
import { useProjectStore } from "@/store/useProjectStore";
import { useNavigate } from "react-router-dom";
import { useGetEmailConfigs } from "@blocks-communication/mail/hooks/use-email-config";
import { langConfigureData } from "@blocks-localization/constants/language-dummy-data";
import {
  useGetEmailTemplate,
  useSendTestMail,
} from "@blocks-communication/mail/hooks/use-email-template";
import { EmailTemplateDetailsSkeleton } from "./email-template-details-skeleton";

export function EmailCommunicationDetails({ params, onBack }: { params: { id: string }; onBack?: () => void }) {
  const { id } = params;
  const { isLoading, isFetching, data } = useGetEmailTemplate(id);
  const { data: loggedInUser } = useGetUser();
  const [emailDetails, setEmailDetails] = useState<IEmailTemplate | null>(null);
  const {
    isLoading: isConfigsLoading,
    isFetching: isConfigsFetching,
    data: emailConfigsData,
  } = useGetEmailConfigs(0, 100);
  const { isPending, mutateAsync } = useSendTestMail();
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  const navigate = useNavigate();
  const [isEditDialogOpen, setIsEditDialogOpen] = useState(false);
  const [isSendTestEmailModalOpen, setIsSendTestEmailModalOpen] = useState(false);

  const sendTestEmailModalOpen = () => {
    setIsSendTestEmailModalOpen(true);
  };

  useEffect(() => {
    if (id) {
      const email = data;
      setEmailDetails(email || null);
    }
  }, [id, data]);

  if (!emailDetails || isLoading || isFetching || isConfigsLoading || isConfigsFetching) {
    return <EmailTemplateDetailsSkeleton />;
  }
  BREADCRUMB_CUSTOM_TITLES["/utilities/email/communications"] = "Email Templates";
  BREADCRUMB_CUSTOM_TITLES["/utilities/email/communications/" + emailDetails?.itemId] =
    emailDetails?.name ? emailDetails.name : "";

  const confirmationModalData = {
    dialogTitle: "Send test email",
    dialogSubtitle: "Are you sure you want to send a test email?",
    confirmButton: "Send",
    cancelButton: "Cancel",
  };
  const dat: IEmailTemplate = {
    itemId: "",
    createdDate: "",
    lastUpdatedDate: "",
    createdBy: "",
    lastUpdatedBy: "",
    organizationIds: [],
    tags: [],
    mailConfigurationId: "",
    templateBody: "",
    jsonContent: "",
    imageId: "",
    imageUrl: "",
    language: "",
    name: "",
    templateSubject: "",
    generatedBy: "",
  };
  const editData = emailDetails ? emailDetails : dat;

  const sendTestEmail = async () => {
    try {
      console.log(emailDetails);
      const payload = {
        to: loggedInUser?.data?.email || "",
        purpose: emailDetails.name || "",
        language: emailDetails.language || "",
        projectKey: tenantId,
      };
      const res = await mutateAsync(payload);
      if (res?.isSuccess) {
        toast({
          variant: "success",
          title: "Success",
          description: "Sent test email successfully",
        });

        setIsSendTestEmailModalOpen(false);
      } else {
        toast({
          variant: "destructive",
          title: "Error",
          description: JSON.stringify(res?.errors),
        });

        setIsSendTestEmailModalOpen(false);
      }
    } catch (error) {
      toast({
        variant: "destructive",
        title: "Error",
        description: JSON.stringify(error),
      });

      setIsSendTestEmailModalOpen(false);
    }
  };

  return (
    <div>
      <div className="hidden md:flex">
        <PageBreadcrumb breadcrumbIndex={3} />
      </div>
      <div className="mt-5 flex items-center justify-between">
        <div className="item-center flex gap-2">
          <Button size="icon" variant="ghost" className="h-8 w-8" onClick={() => onBack ? onBack() : navigate(-1)}>
            <ArrowLeft className="h-6 w-6" />
          </Button>
          <h1 className="text-lg font-semibold md:text-2xl">{emailDetails.name}</h1>
        </div>
        <div className="flex gap-4">
          <div>
            <Button
              size="default"
              variant="outline"
              className="gap-2 shadow-none hover:bg-white"
              disabled={isPending}
              onClick={sendTestEmailModalOpen}
            >
              <Send className="h-5 w-5" />
              <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">Send test Email</span>
            </Button>
            <Dialog open={isSendTestEmailModalOpen} onOpenChange={setIsSendTestEmailModalOpen}>
              <ConfirmationModal
                onCancel={() => {
                  setIsSendTestEmailModalOpen(false);
                }}
                onConfirm={sendTestEmail}
                data={confirmationModalData}
                buttonState={{ confirm: { disable: isPending } }}
              />
            </Dialog>
          </div>
        </div>
      </div>
      <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-3">
        <div className="rounded-sm border border-gray-200 bg-white shadow-none dark:border-gray-700 dark:bg-gray-800 lg:col-span-2">
          <CardHeader>
            <div className="flex w-full items-center justify-between px-4 pt-4">
              <CardTitle className="text-xl">Template</CardTitle>
              <Button
                size="default"
                variant="outline"
                className="gap-2 shadow-none hover:bg-white"
                onClick={() =>
                  navigate(`/utilities/email/communications/${emailDetails.itemId}/edit`)
                }
              >
                <Pencil className="h-5 w-5" />
                <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">Edit</span>
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <div className="grid gap-1 border-t">
              <iframe
                srcDoc={emailDetails.templateBody}
                style={{ width: "100%", height: "60vh" }}
              />
            </div>
          </CardContent>
        </div>
        <div className="rounded-sm border border-gray-200 bg-white shadow-none dark:border-gray-700 dark:bg-gray-800">
          <CardHeader>
            <div className="flex w-full items-center justify-between px-4 pt-4">
              <CardTitle className="text-xl">Details</CardTitle>
              <Dialog open={isEditDialogOpen} onOpenChange={setIsEditDialogOpen}>
                <DialogTrigger asChild>
                  <Button
                    size="default"
                    variant="outline"
                    className="gap-2 shadow-none hover:bg-white"
                    onClick={() => setIsEditDialogOpen(true)}
                  >
                    <Pencil className="h-5 w-5" />
                    <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">Edit</span>
                  </Button>
                </DialogTrigger>
                <EditCommunication
                  dialogTitle="Edit Template"
                  templateData={editData}
                  onClose={() => {
                    setIsEditDialogOpen(false);
                  }}
                />
              </Dialog>
            </div>
          </CardHeader>

          <CardContent>
            <div className="border-t px-4 pt-4">
              <div className="mb-10">
                <h3 className="text-sm font-medium text-low-emphasis">Subject</h3>
                <p className="text-base font-normal text-high-emphasis">
                  {emailDetails.templateSubject}
                </p>
              </div>
            </div>
            <div className="grid grid-cols-2 gap-4 px-4">
              <div className="grid gap-10">
                <div className="grid gap-1">
                  <h3 className="text-sm font-medium text-low-emphasis">Language</h3>
                  <p className="text-base font-normal text-high-emphasis">
                    {
                      langConfigureData.find(
                        (lang) =>
                          lang.itemId.split("-")[0] === (emailDetails.language ?? "").split("-")[0],
                      )?.languageName
                    }
                  </p>
                </div>
                <div className="grid gap-1">
                  <h3 className="text-sm font-medium text-low-emphasis">Created on</h3>
                  <p className="text-base font-normal text-high-emphasis">
                    {!emailDetails ||
                      !emailDetails.createdDate ||
                      !checkValidDate(emailDetails.createdDate)
                      ? "-"
                      : formatFullDate(parseDateString(emailDetails.createdDate))}
                    {/* {formatDate(emailDetails?.createdDate? new Date(emailDetails?.createdDate) : new Date(), true)} */}
                  </p>
                </div>
              </div>
              <div>
                <div className="grid gap-10">
                  <div className="grid gap-1">
                    <h3 className="text-sm font-medium text-low-emphasis">Configuration</h3>
                    <p className="text-base font-normal text-high-emphasis">
                      {
                        emailConfigsData?.find(
                          (config) => config.itemId === emailDetails.mailConfigurationId,
                        )?.name
                      }
                    </p>
                  </div>
                  <div className="grid gap-1">
                    <h3 className="text-sm font-medium text-low-emphasis">Last modified</h3>
                    <p className="text-base font-normal text-high-emphasis">
                      {/* {formatDate(emailDetails?.lastUpdatedDate? new Date(emailDetails?.lastUpdatedDate) : new Date(), false)} */}
                      {!emailDetails ||
                        !emailDetails.lastUpdatedDate ||
                        !checkValidDate(emailDetails.lastUpdatedDate)
                        ? "-"
                        : formatFullDate(parseDateString(emailDetails.lastUpdatedDate))}
                    </p>
                  </div>
                </div>
              </div>
            </div>
          </CardContent>
        </div>
      </div>
    </div>
  );
}
