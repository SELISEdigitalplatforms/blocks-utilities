import React from "react";
import { Paperclip } from "lucide-react";
import {
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogContent,
} from "@/components/ui-kits/dialog/dialog";
import { Button } from "@/components/ui-kits/button/button";
import { ArrowDownToLine, CloudUpload } from "lucide-react";
import { useState } from "react";
import {
  FileUploader,
  FileUploaderContent,
  FileUploaderItem,
  FileInput,
} from "@/components/file-uploader/file-uploader";

const FileSvgDraw = () => {
  return (
    <>
      <div className="text-gmedium-emphasis mb-3 h-8 w-8">
        <CloudUpload />
      </div>
      <p className="mb-1 text-sm text-high-emphasis">
        <span className="font-semibold text-primary">Click to upload</span>
        &nbsp; or drag and drop
      </p>
      <p className="text-xs text-low-emphasis">SVG, PNG, JPG or GIF</p>
    </>
  );
};

export default function ImportCommunications() {
  const [files, setFiles] = useState<File[] | null>(null);

  const dropZoneConfig = {
    maxFiles: 5,
    maxSize: 1024 * 1024 * 4,
    multiple: true,
  };

  return (
    <DialogContent className="rounded-md sm:max-w-[450px]">
      <DialogHeader>
        <DialogTitle className="text-left">Import communications</DialogTitle>
        <DialogDescription className="text-left">
          <div>Lorem ipsum dolor sit amet consectetur.</div>
          <FileUploader
            value={files}
            onValueChange={setFiles}
            dropzoneOptions={dropZoneConfig}
            className="relative mb-2 mt-6 rounded-lg bg-background p-[0.5px]"
          >
            <FileInput className="rounded-md outline-dashed outline-1 outline-border-default">
              <div className="flex w-full flex-col items-center justify-center py-4">
                <FileSvgDraw />
              </div>
            </FileInput>
            <FileUploaderContent>
              {files &&
                files.length > 0 &&
                files.map((file, i) => (
                  <FileUploaderItem key={i} index={i}>
                    <Paperclip className="h-4 w-4 stroke-current" />
                    <span>{file.name}</span>
                  </FileUploaderItem>
                ))}
            </FileUploaderContent>
          </FileUploader>
        </DialogDescription>
      </DialogHeader>

      <DialogFooter className="mr-1 grid grid-cols-2 gap-2">
        <div className="mt-2 flex flex-row gap-2 text-primary">
          <ArrowDownToLine size={20} />
          <h3 className="text-sm font-medium">File Template</h3>
        </div>
        <div className="grid grid-cols-2 gap-2">
          <Button variant="outline" size="default">
            Cancel
          </Button>
          <Button size="default" className="bg-primary">
            Upload
          </Button>
        </div>
      </DialogFooter>
    </DialogContent>
  );
}
