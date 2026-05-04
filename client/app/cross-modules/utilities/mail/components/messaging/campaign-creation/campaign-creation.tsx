import React from "react";
import { AtSign } from "lucide-react";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import {
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogContent,
} from "@/components/ui-kits/dialog/dialog";
import { Button } from "@/components/ui-kits/button/button";
import { DialogTrigger } from "@radix-ui/react-dialog";
import { Textarea } from "@/components/ui-kits/textarea/textarea";

interface CampaignCreationProps {
  dialogTitle: string;
  data?: string[];
}

const CampaignCreation: React.FC<CampaignCreationProps> = ({ dialogTitle }) => (
  <DialogContent className="rounded-md sm:max-w-[700px]">
    <DialogHeader>
      <DialogTitle className="text-left">{dialogTitle}</DialogTitle>
      <DialogDescription> </DialogDescription>
      <hr />

      <div className="py-4 text-left">
        <div className="grid grid-cols-2 gap-4">
          <div>
            <Label htmlFor="campaignName" className="text-left font-medium text-high-emphasis">
              Campaign Name
            </Label>
            <Input
              id="campaignName"
              placeholder="Enter Campaign Name"
              className="border-default col-span-3 mt-1 border shadow-none"
            />
          </div>
          <div>
            <Label htmlFor="configuration" className="text-left font-medium text-high-emphasis">
              Configuration
            </Label>
            <Input
              id="configuration"
              placeholder="Enter Configuration"
              className="border-default col-span-3 mt-1 border shadow-none"
            />
          </div>
        </div>
        <div>
          <div className="mt-3 flex justify-between">
            <Label htmlFor="configName" className="mt-3 text-left font-medium text-high-emphasis">
              Message
            </Label>
            <Button size="sm" variant="outline" className="gap-2">
              <AtSign className="h-3.5 w-3.5" />
              <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">Merge tags</span>
            </Button>
          </div>

          <Textarea
            id="configName"
            className="border-default col-span-3 mt-1 min-h-[116px] border shadow-none"
            placeholder="Type your message here."
          />
        </div>
      </div>
    </DialogHeader>

    <DialogFooter className="mt-[-14px] flex flex-row gap-2">
      <DialogTrigger asChild>
        <Button variant="outline" size="default">
          Cancel
        </Button>
      </DialogTrigger>
      <Button size="default">Save</Button>
    </DialogFooter>
  </DialogContent>
);
export default CampaignCreation;
