

import React, { useState } from "react";
import { Pencil, Trash } from "lucide-react";
import DeleteEmailConfig from "@blocks-communication/mail/components/email-service/modals/delete-email-config/delete-email-config";
import NewConfiguration from "@blocks-communication/mail/components/email-service/modals/new-configuration/new-configuration";
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
import { IEmailConfig, MailServiceProvider } from "@blocks-communication/mail/models/email";
import { useGetEmailConfigs } from "@blocks-communication/mail/hooks/use-email-config";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";

interface EmailConfigurationProps {
  addConfigOpen?: boolean;
  onAddConfigOpenChange?: (open: boolean) => void;
}

export function EmailConfiguration({ addConfigOpen, onAddConfigOpenChange }: EmailConfigurationProps = {}) {
  const [internalOpen, setInternalOpen] = useState<boolean>(false);
  const open = addConfigOpen !== undefined ? addConfigOpen : internalOpen;
  const setOpen = onAddConfigOpenChange || setInternalOpen;
  const [isEditOpen, setIsEditOpen] = useState<boolean>(false);
  const [deleteModalOpen, setDeleteModalOpen] = useState<boolean>(false);

  const isMediumScreen = useMediaQuery(`(max-width: 1180px)`);
  const isMobileScreen = useMediaQuery(`(max-width: 768px)`);

  const [filterData] = useState({ pageNumber: 0, pageSize: 10 });
  const { isLoading, data } = useGetEmailConfigs(filterData.pageNumber, filterData.pageSize);

  // const [loading, setLoading] = useState(true);
  // const [error, setError] = useState<string | null>(null);

  // useEffect(() => {
  //   const fetchData = async () => {
  //     try {
  //       // setLoading(true);
  //       // setError(null);
  //       // const response = await fetch(
  //       //   `/cloudconfiguration/v1/Mail/Get?ProjectKey=${tenantId}&configurationName=Default`,
  //       // );
  //       // if (!response.ok) {
  //       //   throw new Error(`HTTP error! status: ${response.status}`);
  //       // }
  //       // const result = await response.json();
  //       // const configs = Array.isArray(result) ? result : [result];
  //       // setData(configs);
  //       const response = await getEmailConfigs();
  //       setData(response);
  //       console.log(data);
  //     } catch (err) {
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

  if (isLoading) {
    return (
      <div className="grid gap-2">
        {Array.from({ length: 6 }).map((_, index) => (
          <Skeleton key={index} className="h-12 w-full rounded" />
        ))}
      </div>
    );
  }

  // if (error) {
  //   return (
  //     <div className="p-4">
  //       <div className="rounded border border-red-200 bg-red-50 p-4">
  //         <h2 className="font-medium text-red-800">Error</h2>
  //         <p className="text-red-600">{error}</p>
  //       </div>
  //     </div>
  //   );
  // }

  return (
    <div>
      <Dialog open={open} onOpenChange={setOpen}>
        <NewConfiguration dialogTitle="Add Configuration" onClose={() => setOpen(false)} isEdit={false} />
      </Dialog>
      {data && data.length > 0 ? (
        <Accordion type="single" collapsible className="mt-6" defaultValue={data[0].itemId}>
          {data.map((config: IEmailConfig, index: number) => (
            <AccordionItem
              key={config.itemId}
              value={config.itemId}
              className={`rounded-sm border bg-background px-4 ${index > 0 ? "mt-6" : ""}`}
            >
              <AccordionTrigger className="text-xl font-semibold hover:no-underline">
                <div className="flex items-center justify-between w-full pr-8">
                  <span>{config.name}</span>
                  <div className="flex gap-1">
                    {!config.isDefault && (<Dialog open={isEditOpen} onOpenChange={setIsEditOpen}>
                      <DialogTrigger asChild>
                        <Button size="sm" variant="outline" className="h-9 gap-2 px-4 py-1" onClick={(e) => e.stopPropagation()}>
                          <Pencil className="h-3.5 w-3.5" />
                          <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">Edit</span>
                        </Button>
                      </DialogTrigger>
                      <NewConfiguration
                        dialogTitle="Edit Configuration"
                        previousData={config}
                        isEdit={true}
                        onClose={() => setIsEditOpen(false)}
                      />
                    </Dialog>)}
                    {!config.isDefault && (
                      <Dialog open={deleteModalOpen} onOpenChange={setDeleteModalOpen}>
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
                          onClose={() => setDeleteModalOpen(false)}
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
                      <p className="text-base">{config.isInbound ? "Inbound" : "Outbound"}</p>
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
                        <p className="text-sm text-muted-foreground">Username</p>
                        <p className="text-base">{config.senderUserName}</p>
                      </div>
                      <div>
                        <p className="text-sm text-muted-foreground">Account Password</p>
                        <p className="trucate break-all text-base">*********************</p>
                      </div>
                    </>
                  ) : (
                    <>
                      <div>
                        <p className="text-sm text-muted-foreground">Sender name</p>
                        <p className="text-base">{config.senderName}</p>
                      </div>
                      <div>
                        <p className="text-sm text-muted-foreground">Sender address</p>
                        <p className="text-base">{config.senderAddress}</p>
                      </div>
                    </>
                  )}
                  <div>
                    <p className="text-sm text-muted-foreground">Provider</p>
                    <p className="text-base">{MailServiceProvider[config.provider]}</p>
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
                      <p className="text-sm text-muted-foreground">Sender username</p>
                      <p className="text-base">{config.senderUserName}</p>
                    </div>
                    <div>
                      <p className="text-sm text-muted-foreground">Account Password</p>
                      {/* <p className="trucate break-all text-base">{config.accountPassword}</p> */}
                      <p className="trucate break-all text-base">*********************</p>
                    </div>
                  </div>
                )}
              </AccordionContent>
            </AccordionItem>
          ))}
        </Accordion>
      ) : (
        <div className="rounded-lg border border-dashed p-8 text-center text-muted-foreground bg-background">
          <p>No email configurations found. Use the Add Configuration button above to create one.</p>
        </div>
      )}
    </div>
  );
}
