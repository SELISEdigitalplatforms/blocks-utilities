import React from "react";
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

interface MessageConfigurationProps {
  dialogTitle: string;
  data?: string[];
}

const MessageConfiguration: React.FC<MessageConfigurationProps> = ({ dialogTitle }) => (
  <DialogContent className="rounded-md sm:max-w-[700px]">
    <DialogHeader>
      <DialogTitle className="mb-2 text-left">{dialogTitle}</DialogTitle>
      <hr />
      <DialogDescription asChild>
        <div className="py-4 text-left">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <Label htmlFor="protocol" className="text-left font-medium text-high-emphasis">
                Protocol
              </Label>
              <Input
                id="protocol"
                placeholder="Enter host"
                className="border-default col-span-3 mt-1 border shadow-none"
              />
            </div>
            <div>
              <Label htmlFor="serviceVendor" className="text-left font-medium text-high-emphasis">
                Service Vendor
              </Label>
              <Input
                id="serviceVendor"
                placeholder="Enter port"
                className="border-default col-span-3 mt-1 border shadow-none"
              />
            </div>
          </div>
          <div className="mt-3 grid grid-cols-2 gap-4">
            <div>
              <Label htmlFor="configName" className="text-left font-medium text-high-emphasis">
                Configuration name
              </Label>
              <Input
                id="configName"
                placeholder="Enter configuration name"
                className="border-default col-span-3 mt-1 border shadow-none"
              />
            </div>
            <div>
              <Label htmlFor="id" className="text-left font-medium text-high-emphasis">
                Configuration ID
              </Label>
              <Input
                id="id"
                placeholder="Enter configuration ID"
                className="border-default col-span-3 mt-1 border shadow-none"
              />
            </div>
          </div>
          <div className="mt-3 grid grid-cols-2 gap-4">
            <div>
              <Label
                htmlFor="authenticationToken"
                className="text-left font-medium text-high-emphasis"
              >
                Authentication Token
              </Label>
              <Input
                id="authenticationToken"
                placeholder="Enter username"
                className="border-default col-span-3 mt-1 border shadow-none"
              />
            </div>
            <div>
              <Label htmlFor="AccountID" className="text-left font-medium text-high-emphasis">
                AccountID
              </Label>
              <Input
                id="AccountID"
                placeholder="Enter password"
                className="border-default col-span-3 mt-1 border shadow-none"
              />
            </div>
          </div>
          <div className="mt-4 grid grid-cols-2 gap-4">
            <div>
              <Label htmlFor="username" className="text-left font-medium text-high-emphasis">
                Sender
              </Label>
              <Input
                id="username"
                placeholder="Enter username"
                className="border-default col-span-3 mt-1 border shadow-none"
              />
            </div>
            <div>
              <Label
                htmlFor="numberLookupEndURI"
                className="text-left font-medium text-high-emphasis"
              >
                Number Lookup End URI
              </Label>
              <Input
                id="numberLookupEndURI"
                placeholder="Enter password"
                className="border-default col-span-3 mt-1 border shadow-none"
              />
            </div>
          </div>
        </div>
      </DialogDescription>
    </DialogHeader>

    <DialogFooter className="flex flex-row gap-2">
      <DialogTrigger asChild>
        <Button variant="outline" size="default">
          Cancel
        </Button>
      </DialogTrigger>
      <Button size="default">Save</Button>
    </DialogFooter>
  </DialogContent>
);
export default MessageConfiguration;
