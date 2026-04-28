import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { toast } from "@/hooks/use-toast";
import { useProjectStore } from "@/store/useProjectStore";
import { IEmailTemplate } from "@blocks-communication/mail/models/email";
import { useState } from "react";
import { useGetMFAConfig, useSaveMFAConfig } from "@blocks-idp/mfa/hooks/use-mfa-config";
import { useGetEmailTemplates } from "@blocks-communication/mail/hooks/use-email-template";

type ChooseEmailTemplateProps = {
  open: boolean;
  setOpen: (value: boolean) => void;
};

const LoadingSkelton = () => {
  return (
    <>
      {Array.from({ length: 10 }).map((_item, index) => (
        <div className={`w-[150px]`} key={index}>
          <div className={`relative h-[200px] w-full border`}>
            <Skeleton className="h-full w-full" />
          </div>
        </div>
      ))}
    </>
  );
};

export const ChooseEmailTemplate = ({ open, setOpen }: ChooseEmailTemplateProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data: mfaConfigData } = useGetMFAConfig({ projectKey: tenantId });
  const [filter, setFilter] = useState({ page: 0, pageSize: 10 });
  const { data, isLoading, isFetching } = useGetEmailTemplates(filter.page, filter.pageSize, "", "Name", false, "", "");
  const { isPending, mutateAsync } = useSaveMFAConfig();
  const [seletedTemplate, setSelectedTemplate] = useState<IEmailTemplate | null>(null);

  const onSaveHandler = async () => {
    try {
      const userMfaTypes = mfaConfigData?.userMfaType ? [...mfaConfigData.userMfaType] : [];
      const res = await mutateAsync({
        projectKey: tenantId,
        enableMfa: true,
        userMfaType: userMfaTypes,
      });
      if (res.isSuccess) {
        toast({
          variant: "success",
          title: "Success",
          description: "Template successfully selected",
        });
        setOpen(false);
      } else {
        toast({
          variant: "destructive",
          title: "Error",
          description: "Something went wrong",
        });
      }
    } catch (error) {
      toast({
        variant: "destructive",
        title: "Error",
        description: `Something went wrong | ${JSON.stringify((error as { error: { errors: unknown } }).error.errors)}`,
      });
    }
  };

  const loading = isLoading || isFetching;
  return (
    <Dialog
      open={open}
      onOpenChange={(isOpen) => {
        if (!isOpen) setSelectedTemplate(null);
        setOpen(isOpen);
      }}
    >
      <DialogContent className="max-w-4xl">
        <DialogHeader>
          <DialogTitle>Choose a template </DialogTitle>
        </DialogHeader>

        <div className="mt-4 grid grid-cols-5 gap-4">
          {loading ? (
            <LoadingSkelton />
          ) : (
            <>
              <div
                className={`w-[150px]`}
                onClick={() =>
                  setSelectedTemplate({
                    itemId: "",
                    name: "",
                  })
                }
              >
                <div
                  className={`relative h-[200px] w-full border ${seletedTemplate?.itemId === "" ? "border border-primary" : " "}`}
                >
                  <img
                    src={`/assets/images/services/email/email-template-sample-1.png`}
                   
                    alt="email-template"
                  />
                </div>
                <div className="mt-2 flex items-center justify-between text-sm">Default</div>
              </div>
              {data?.templates?.map((template) => (
                <div className={`w-[150px]`} key={template.itemId} onClick={() => setSelectedTemplate(template)}>
                  <div
                    className={`relative h-[200px] w-full border ${seletedTemplate?.itemId === template.itemId ? "border border-primary" : " "}`}
                  >
                    <img
                      src={`/assets/images/services/email/email-template-sample-1.png`}
                     
                      alt="email-template"
                    />
                  </div>
                  <div className="mt-2 flex items-center justify-between text-sm">{template.name}</div>
                </div>
              ))}
            </>
          )}
        </div>
        <div className="flex justify-end">
          <Pagination
            page={filter.page}
            pageSize={filter.pageSize}
            onChange={(page) => {
              setFilter((prev) => ({
                ...prev,
                page,
              }));
            }}
            totalCount={data?.totalCount || 0}
          />
        </div>
        <DialogFooter className="flex gap-2">
          <div>
            <DialogTrigger asChild>
              <Button className="min-w-[80px]" variant="outline" size="default">
                Cancel
              </Button>
            </DialogTrigger>

            <Button
              className="ml-2 min-w-[80px]"
              size="default"
              onClick={onSaveHandler}
              disabled={isPending || !seletedTemplate || isLoading || isFetching}
            >
              Choose
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
