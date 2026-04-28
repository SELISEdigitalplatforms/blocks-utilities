
import React from "react";
import { Copy, Pencil, Plus, Trash } from "lucide-react";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui-kits/accordion/accordion";
import { Button } from "@/components/ui-kits/button/button";
import { Dialog, DialogTrigger } from "@/components/ui-kits/dialog/dialog";
import DeleteEmailConfig from "@blocks-communication/mail/components/email-service/modals/delete-email-config/delete-email-config";
import MessageConfiguration from "@blocks-communication/mail/components/messaging/message-configuration/message-configuration";

interface MessagingConfig {
  id: string;
  title: string;
  protocol: string;
  serviceVendor: string;
  authenticationToken: string;
  accountID: string;
  sender: string;
  numberLookupEndURI: string;
}

const msgConfigurations: MessagingConfig[] = [
  {
    id: "default",
    title: "Default",
    protocol: "SMS",
    serviceVendor: "Vendor Name",
    authenticationToken: "e728423xae0njf5502",
    accountID: "22718945",
    sender: "Jordyn Workman",
    numberLookupEndURI: "EE85JKL",
  },
  {
    id: "custome1",
    title: "Custom Configuration 1",
    protocol: "SMS",
    serviceVendor: "Vendor Name",
    authenticationToken: "e728423xae0njf5502",
    accountID: "22718945",
    sender: "Jordyn Workman",
    numberLookupEndURI: "EE85JKL",
  },
  {
    id: "custome2",
    title: "Custom Configuration 2",
    protocol: "SMS",
    serviceVendor: "Vendor Name",
    authenticationToken: "e728423xae0njf5502",
    accountID: "22718945",
    sender: "Jordyn Workman",
    numberLookupEndURI: "EE85JKL",
  },
];

export function MessagingConfiguration() {
  return (
    <div>
      <div className="hidden md:flex">
        <PageBreadcrumb breadcrumbIndex={2} />
      </div>
      <div className="mt-5 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Configure Messaging</h1>
        <div>
          <Dialog>
            <DialogTrigger asChild>
              <Button size="sm" className="h-10 gap-2 px-4 py-1">
                <Plus className="h-5 w-5" />
                <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">
                  New Configuration
                </span>
              </Button>
            </DialogTrigger>
            <MessageConfiguration dialogTitle="New Configuration" data={[]} />
          </Dialog>
        </div>
      </div>
      <Accordion type="single" collapsible className="mt-6" defaultValue={msgConfigurations[0]?.id}>
        {msgConfigurations.map((config, index) => (
          <AccordionItem
            key={config.id}
            value={config.id}
            className={`rounded-sm border bg-background px-4 ${index > 0 ? "mt-6" : ""}`}
          >
            <AccordionTrigger className="text-xl font-semibold">{config.title}</AccordionTrigger>
            <AccordionContent>
              {config.id !== "default" && (
                <div className="flex gap-1">
                  <Dialog>
                    <DialogTrigger asChild>
                      <Button
                        size="sm"
                        variant="outline"
                        className="h-9 gap-2 px-4 py-1 text-red-500 hover:bg-red-400 hover:text-white"
                      >
                        <Trash className="h-3.5 w-3.5" />
                        <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">Delete</span>
                      </Button>
                    </DialogTrigger>
                    <DeleteEmailConfig onClose={() => {}} configId={config.id} />
                  </Dialog>

                  <Dialog>
                    <DialogTrigger asChild>
                      <Button size="sm" variant="outline" className="h-9 gap-2 px-4 py-1">
                        <Pencil className="h-3.5 w-3.5" />
                        <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">Edit</span>
                      </Button>
                    </DialogTrigger>
                    <MessageConfiguration dialogTitle="Edit Configuration" data={[]} />
                  </Dialog>

                  <Button size="sm" variant="outline" className="h-9 gap-2 px-4 py-1">
                    <Copy className="h-3.5 w-3.5" />
                    <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">Duplicate</span>
                  </Button>
                </div>
              )}
              <div className="mt-5 grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
                <div>
                  <p className="text-sm text-muted-foreground">Protocol</p>
                  <p className="text-base">{config.protocol}</p>
                </div>
                <div>
                  <p className="text-sm text-muted-foreground">Service Vendor</p>
                  <p className="text-base">{config.serviceVendor}</p>
                </div>
                <div>
                  <p className="text-sm text-muted-foreground">Authentication Token</p>
                  <p className="text-base">{config.authenticationToken}</p>
                </div>
                <div>
                  <p className="text-sm text-muted-foreground">AccountID</p>
                  <p className="text-base">{config.accountID}</p>
                </div>
                <div>
                  <p className="text-sm text-muted-foreground">Sender</p>
                  <p className="text-base">{config.sender}</p>
                </div>
                <div>
                  <p className="text-sm text-muted-foreground">Number Lookup End URI</p>
                  <p className="text-base">{config.numberLookupEndURI}</p>
                </div>
              </div>
            </AccordionContent>
          </AccordionItem>
        ))}
      </Accordion>
    </div>
  );
}
