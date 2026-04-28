

import React, { useEffect, useState } from "react";
import { Pencil } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { IMessagingServiceData } from "@blocks-communication/mail/models/messaging";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { Dialog, DialogTrigger } from "@/components/ui-kits/dialog/dialog";
import CampaignCreation from "@blocks-communication/mail/components/messaging/campaign-creation/campaign-creation";
import { formatDate } from "@/lib/utils";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { messagingServiceData } from "@blocks-communication/mail/constants/messaging";

export function CampaignDetails({ params }: { params: { id: string } }) {
  const { id } = params;
  const [messageDetails, setMessageDetails] = useState<IMessagingServiceData | null>(null);

  useEffect(() => {
    if (id) {
      const messageId = Array.isArray(id) ? id[0] : id;
      const message = messagingServiceData.find((message) => message.id === messageId);
      setMessageDetails(message || null);
    }
  }, [id]);

  if (!messageDetails) {
    return <div>Loading...</div>;
  }
  BREADCRUMB_CUSTOM_TITLES["/utilities/messaging/campaigns"] = "Messaging Messages";
  BREADCRUMB_CUSTOM_TITLES["/utilities/messaging/campaigns/" + messageDetails?.id] =
    messageDetails.name;

  return (
    <div>
      <div className="hidden md:flex">
        <PageBreadcrumb breadcrumbIndex={3} />
      </div>
      <div className="mt-5 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">{messageDetails.name}</h1>
        <Dialog>
          <DialogTrigger asChild>
            <Button size="sm" className="h-10 gap-2 px-4 py-1 shadow-none">
              <Pencil className="h-5 w-5" />
              <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">Edit Messages</span>
            </Button>
          </DialogTrigger>
          <CampaignCreation dialogTitle="Edit Messages" />
        </Dialog>
      </div>
      <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-3">
        <Card
          className="rounded-sm shadow-none lg:col-span-2"
          style={{ minHeight: 0, height: "auto" }}
        >
          <CardHeader>
            <CardTitle className="text-xl">Message</CardTitle>
          </CardHeader>
          <CardContent className="text-base">
            Lorem ipsum dolor sit amet consectetur. Dignissim placerat id suscipit amet aliquam
            malesuada aliquam fusce commodo. Dictum commodo ultrices dui odio eget. Vel amet
            vestibulum viverra purus risus enim eu nibh. Lorem blandit leo risus dolor.
          </CardContent>
        </Card>
        <Card className="rounded-sm shadow-none">
          <CardHeader>
            <CardTitle className="text-xl">About</CardTitle>
          </CardHeader>
          <CardContent className="grid grid-cols-2 gap-4">
            <div className="grid gap-10">
              <div className="grid gap-1">
                <h3 className="text-sm font-medium text-low-emphasis">Configuration</h3>
                <p className="text-base font-normal text-high-emphasis">
                  {messageDetails.configuration}
                </p>
              </div>
              <div className="grid gap-1">
                <h3 className="text-sm font-medium text-low-emphasis">Created on</h3>
                <p className="text-base font-normal text-high-emphasis">
                  {formatDate(messageDetails.createdOn, true)}
                </p>
              </div>
              <div className="grid gap-1">
                <h3 className="text-sm font-medium text-low-emphasis">Last modified</h3>
                <p className="text-base font-normal text-high-emphasis">
                  {formatDate(messageDetails.lastModified)}
                </p>
              </div>
            </div>
            <div>
              <div className="grid gap-10">
                <div className="grid gap-1">
                  <h3 className="text-sm font-medium text-low-emphasis">Protocol</h3>
                  <p className="text-base font-normal text-high-emphasis">
                    {messageDetails.protocol}
                  </p>
                </div>
                <div className="grid gap-1">
                  <h3 className="text-sm font-medium text-low-emphasis">Created by</h3>
                  <p className="text-base font-normal text-high-emphasis">
                    {messageDetails.createdBy}
                  </p>
                </div>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
