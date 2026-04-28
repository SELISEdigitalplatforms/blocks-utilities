

import { Button } from "@/components/ui-kits/button/button";
// import BeePlugin from "@blocks-communication/mail/components/bee-plugin-starter/bee-plugin";
import BeePluginStarter from "@blocks-communication/mail/components/bee-plugin-starter/bee-plugin-starter";
import { useState, useEffect, useRef } from "react";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { IEmailTemplate } from "@blocks-communication/mail/models/email";
import { useNavigate } from "react-router-dom";
import {
  useGetEmailTemplate,
  useSaveEmailTemplate,
} from "@blocks-communication/mail/hooks/use-email-template";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";

export function EditEmailTemplate({ params }: { params: { id: string } }) {
  const { id } = params;
  const { isLoading, isFetching, data } = useGetEmailTemplate(id);
  const [emailDetails, setEmailDetails] = useState<IEmailTemplate | null>(null);
  const { saveEmailTemplate, isPending } = useSaveEmailTemplate();
  const beeRef = useRef<{ submit: () => void; preview: () => void; reset: () => void }>();
  const [, setTemplateData] = useState<IEmailTemplate>({
    itemId: "",
  });
  const navigate = useNavigate();

  useEffect(() => {
    if (id) {
      const email = data;
      setEmailDetails(email || null);
    }
  }, [id, data]);

  if (!emailDetails || isLoading || isFetching) {
    return (
      <div>
        <div className="hidden md:flex">
          <Skeleton className="h-6 w-32 rounded" />
          <Skeleton className="ml-4 h-6 w-48 rounded" />
        </div>
        <div className="mt-5">
          <div className="flex items-center justify-between">
            <Skeleton className="h-8 w-1/3 rounded" />
            <div className="flex gap-2">
              <Skeleton className="h-10 w-20 rounded" />
              <Skeleton className="h-10 w-20 rounded" />
            </div>
          </div>
          <div className="mt-4 rounded-lg border border-gray-200 bg-white shadow-sm dark:border-gray-700 dark:bg-gray-800">
            <Skeleton className="h-80 w-full rounded" />
          </div>
        </div>
      </div>
    );
  }

  const handleBeePluginData = async (data: { htmlFile: string; jsonFile: string }) => {
    console.log("newsletter-template.html", data.htmlFile);
    console.log("newsletter-template.json", data.jsonFile);
    const currentData: IEmailTemplate = {
      itemId: emailDetails?.itemId || "",
      templateBody: data.htmlFile,
      jsonContent: data.jsonFile,
    };
    await saveEmailTemplate(currentData);
    setTemplateData(currentData);
    navigate(`/utilities/email/communications/${emailDetails.itemId}`);
  };

  return (
    <div>
      <div className="hidden md:flex">
        <PageBreadcrumb breadcrumbIndex={3} />
      </div>
      <div>
        <div className="mb-[20px] mt-[16px] flex items-center justify-between">
          <h3 className="text-3xl font-semibold tracking-tight">{emailDetails.name}</h3>
          <div className="flex gap-2">
            <Button
              variant="outline"
              size="lg"
              className="gap-1 text-sm font-medium"
              disabled={isLoading || isFetching}
              onClick={() => beeRef?.current?.reset()}
            >
              <span className="sr-only sm:not-sr-only">Reset</span>
            </Button>
            <Button
              variant="outline"
              size="lg"
              className="gap-1 text-sm font-medium"
              disabled={isLoading || isFetching}
              onClick={() => beeRef?.current?.preview()}
            >
              <span className="sr-only sm:not-sr-only">Preview</span>
            </Button>
            <Button
              disabled={isPending || isLoading || isFetching}
              size="lg"
              onClick={() => {
                beeRef?.current?.submit();
              }}
            >
              Save
            </Button>
          </div>
        </div>
        <div className="mb-8 rounded-lg border border-gray-200 bg-white shadow-sm dark:border-gray-700 dark:bg-gray-800">
          {/* <BeePlugin
                        beeUID="selise-ecap-bee-plugin-uid-dev-stg"
                        mergeTags={[]}
                        specialLinks={[]}
                        onBeeSave={handleBeePluginData}
                        ref={beeRef}
                        jsonFile={emailDetails.jsonContent}
                    /> */}
          <BeePluginStarter
            onBeeSave={handleBeePluginData}
            ref={beeRef}
            jsonFile={emailDetails.jsonContent ? JSON.parse(emailDetails.jsonContent) : undefined}
          />
        </div>
        <div className="mb-4"></div>
      </div>
    </div>
  );
}
