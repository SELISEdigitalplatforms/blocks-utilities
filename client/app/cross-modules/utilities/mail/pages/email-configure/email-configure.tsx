import React, { useState } from "react";
import { ArrowLeft, Pencil, PlusCircle, Trash } from "lucide-react";
import DeleteEmailConfig from "@blocks-utilities/mail/components/email-service/modals/delete-email-config/delete-email-config";
import NewConfiguration from "@blocks-utilities/mail/components/email-service/modals/new-configuration/new-configuration";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui-kits/accordion/accordion";
import { Button } from "@/components/ui-kits/button/button";
import { Dialog, DialogTrigger } from "@/components/ui-kits/dialog/dialog";
import { useMediaQuery } from "@/components/ui-kits/stepper/use-media-query";
import { cn } from "@/lib/utils";
import {
  IEmailConfig,
  MailServiceProvider,
} from "@blocks-utilities/mail/models/email";
import { useGetEmailConfigs } from "@blocks-utilities/mail/hooks/use-email-config";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useNavigate } from "react-router-dom";

interface EmailConfigurationProps {
  addConfigOpen?: boolean;
  onAddConfigOpenChange?: (open: boolean) => void;
}

export function EmailConfiguration({
  addConfigOpen,
  onAddConfigOpenChange,
}: EmailConfigurationProps = {}) {
  const navigate = useNavigate();
  const [internalOpen, setInternalOpen] = useState<boolean>(false);
  const open = addConfigOpen !== undefined ? addConfigOpen : internalOpen;
  const setOpen = onAddConfigOpenChange || setInternalOpen;
  const [editOpenById, setEditOpenById] = useState<Record<string, boolean>>({});
  const [deleteOpenById, setDeleteOpenById] = useState<Record<string, boolean>>(
    {},
  );

  const isMediumScreen = useMediaQuery(`(max-width: 1180px)`);
  const isMobileScreen = useMediaQuery(`(max-width: 768px)`);

  const [pageNumber, setPageNumber] = useState(0);
  const [pageSize] = useState(10);
  const { isLoading, data } = useGetEmailConfigs(pageNumber, pageSize);

  if (isLoading) {
    return (
      <div className="grid gap-2">
        {Array.from({ length: 6 }).map((_, index) => (
          <Skeleton key={index} className="h-12 w-full rounded" />
        ))}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div className="item-center flex gap-2">
          <Button
            size="icon"
            variant="ghost"
            className="h-8 w-8"
            onClick={() => navigate(-1)}
          >
            <ArrowLeft className="h-6 w-6" />
          </Button>
          <h1 className="text-2xl font-semibold">Configure Email</h1>
        </div>
        <div>
          <Dialog open={open} onOpenChange={setOpen}>
            <DialogTrigger asChild>
              <Button size="sm" className="h-10 gap-2 px-4 py-1">
                <PlusCircle className="h-5 w-5" />
                <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">
                  Add Configuration
                </span>
              </Button>
            </DialogTrigger>
            <NewConfiguration
              dialogTitle="Add Configuration"
              onClose={() => setOpen(false)}
              isEdit={false}
            />
          </Dialog>
        </div>
      </div>
      {data && data.length > 0 ? (
        <>
          <Accordion
            type="single"
            collapsible
            className="mt-6"
            defaultValue={data[0].itemId}
          >
            {data.map((config: IEmailConfig, index: number) => (
              <AccordionItem
                key={config.itemId}
                value={config.itemId}
                className={cn(
                  "rounded-sm border bg-background px-4",
                  index > 0 ? "mt-6" : "",
                )}
              >
                <AccordionTrigger className="text-xl font-semibold hover:no-underline">
                  <div className="flex w-full items-center justify-between pr-8">
                    <span>{config.name}</span>
                    <div className="flex gap-1">
                      {!config.isDefault && (
                        <Dialog
                          open={!!editOpenById[config.itemId]}
                          onOpenChange={(val) =>
                            setEditOpenById((prev) => ({
                              ...prev,
                              [config.itemId]: val,
                            }))
                          }
                        >
                          <DialogTrigger asChild>
                            <Button
                              size="sm"
                              variant="outline"
                              className="h-9 gap-2 px-4 py-1"
                              onClick={(e) => e.stopPropagation()}
                            >
                              <Pencil className="h-3.5 w-3.5" />
                              <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">
                                Edit
                              </span>
                            </Button>
                          </DialogTrigger>
                          <NewConfiguration
                            dialogTitle="Edit Configuration"
                            previousData={config}
                            isEdit={true}
                            onClose={() =>
                              setEditOpenById((prev) => ({
                                ...prev,
                                [config.itemId]: false,
                              }))
                            }
                          />
                        </Dialog>
                      )}
                      {!config.isDefault && (
                        <Dialog
                          open={!!deleteOpenById[config.itemId]}
                          onOpenChange={(val) =>
                            setDeleteOpenById((prev) => ({
                              ...prev,
                              [config.itemId]: val,
                            }))
                          }
                        >
                          <DialogTrigger asChild>
                            <Button
                              size="sm"
                              variant="outline"
                              className="h-9 gap-2 px-4 py-1 text-red-500 hover:bg-red-400 hover:text-white"
                              onClick={(e) => e.stopPropagation()}
                            >
                              <Trash className="h-3.5 w-3.5" />
                              <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">
                                Delete
                              </span>
                            </Button>
                          </DialogTrigger>
                          <DeleteEmailConfig
                            configId={config.itemId}
                            onClose={() =>
                              setDeleteOpenById((prev) => ({
                                ...prev,
                                [config.itemId]: false,
                              }))
                            }
                          />
                        </Dialog>
                      )}
                    </div>
                  </div>
                </AccordionTrigger>
                <AccordionContent>
                  <div
                    className={cn(
                      "mt-5 grid grid-cols-3 space-y-2",
                      isMediumScreen && "gap-12",
                      isMobileScreen && "grid-cols-1 gap-6",
                    )}
                  >
                    <div>
                      <p className="text-sm text-muted-foreground">
                        {config.isInbound ? "Server Name" : "Host"}
                      </p>
                      <p className="text-base">{config.host}</p>
                    </div>
                    <div className="ml-1">
                      <p className="text-sm text-muted-foreground">Port</p>
                      <p className="text-base">{config.port}</p>
                    </div>
                    <div>
                      <div className="mb-4">
                        <p className="text-sm text-muted-foreground">Type</p>
                        <p className="text-base">
                          {config.isInbound ? "Inbound" : "Outbound"}
                        </p>
                      </div>
                    </div>
                  </div>
                  <div
                    className={cn(
                      "mt-5 grid grid-cols-3 space-y-2",
                      isMediumScreen && "gap-12",
                      isMobileScreen && "grid-cols-1 gap-6",
                    )}
                  >
                    {config.isInbound ? (
                      <>
                        <div>
                          <p className="text-sm text-muted-foreground">
                            Username
                          </p>
                          <p className="text-base">{config.senderUserName}</p>
                        </div>
                        <div>
                          <p className="text-sm text-muted-foreground">
                            Account Password
                          </p>
                          <p className="trucate break-all text-base">
                            *********************
                          </p>
                        </div>
                      </>
                    ) : (
                      <>
                        <div>
                          <p className="text-sm text-muted-foreground">
                            Sender name
                          </p>
                          <p className="text-base">{config.senderName}</p>
                        </div>
                        <div>
                          <p className="text-sm text-muted-foreground">
                            Sender address
                          </p>
                          <p className="text-base">{config.senderAddress}</p>
                        </div>
                      </>
                    )}
                    <div>
                      <p className="text-sm text-muted-foreground">Provider</p>
                      <p className="text-base">
                        {MailServiceProvider[config.provider]}
                      </p>
                    </div>
                  </div>
                  {!config.isInbound && (
                    <div
                      className={cn(
                        "mt-5 grid grid-cols-3 space-y-2",
                        isMediumScreen && "gap-12",
                        isMobileScreen && "grid-cols-1 gap-6",
                      )}
                    >
                      <div>
                        <p className="text-sm text-muted-foreground">
                          Sender username
                        </p>
                        <p className="text-base">{config.senderUserName}</p>
                      </div>
                      <div>
                        <p className="text-sm text-muted-foreground">
                          Account Password
                        </p>
                        <p className="trucate break-all text-base">
                          *********************
                        </p>
                      </div>
                    </div>
                  )}
                </AccordionContent>
              </AccordionItem>
            ))}
          </Accordion>
        </>
      ) : (
        <div className="rounded-lg border border-dashed bg-background p-8 text-center text-muted-foreground">
          <p>
            No email configurations found. Use the Add Configuration button
            above to create one.
          </p>
        </div>
      )}
    </div>
  );
}
