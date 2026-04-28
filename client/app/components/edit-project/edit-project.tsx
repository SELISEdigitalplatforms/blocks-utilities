import { useState } from "react";
import { Settings } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { EditProjectForm } from "@/components/edit-project-form/edit-project-form";
import { IGetProjectResponse } from "@blocks-identifier/models/project.model";
import { CnameValidatorProject } from "@/components/cname-validator-project/cname-validator-project";
import { formatFullDate } from "@/lib/utils";

interface EditProjectProps {
  data?: IGetProjectResponse;
  isLoading?: boolean;
}

export const EditProject = ({ data, isLoading }: EditProjectProps) => {
  const isCNameNotValidated = !isLoading && data?.data && !data?.data.isDomainVerified;
  const [open, setOpen] = useState<boolean>(false);
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button variant="outline">
          <Settings className="h-4 w-4" />
          <span className="sr-only sm:not-sr-only sm:ml-2">Configure</span>
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Configure domain</DialogTitle>
          {isCNameNotValidated && data?.data.cookieDomain !== "seliseblocks.com" && (
            <div className="flex flex-col items-center justify-between gap-1 rounded-sm border border-base-error bg-blocks-error-100 px-4 py-4 text-base font-normal text-blocks-error-800 md:flex-row">
              <p>No servers found for &apos;{data?.data.cookieDomain}&apos;</p>
              <div className="flex items-center gap-2">
                {data?.data.lastUpdatedDate ? (
                  <p>Reported on {formatFullDate(new Date(data.data.lastUpdatedDate))}</p>
                ) : (
                  <p>Report date unavailable</p>
                )}
                <CnameValidatorProject />
              </div>
            </div>
          )}
          <DialogDescription>Configure your domain to point to your application</DialogDescription>
        </DialogHeader>
        <EditProjectForm onAfterSubmit={() => setOpen(false)} />
      </DialogContent>
    </Dialog>
  );
};
