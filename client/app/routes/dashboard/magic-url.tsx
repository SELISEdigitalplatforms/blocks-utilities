import { MagicUrls } from "@blocks-utilities/magic-url/pages/magic-urls";
import { Button } from "@/components/ui-kits/button/button";
import { Plus } from "lucide-react";
import { useState } from "react";
import { MagicUrlDialog } from "@blocks-utilities/magic-url/components/magic-url-dialog/magic-url-dialog";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";

export default function MagicUrlPage() {
  const [isShortenDialogOpen, setIsShortenDialogOpen] = useState(false);

  return (
    <div className="flex flex-col gap-6 p-6">
      <PageBreadcrumb />
      <div className="flex w-full justify-between text-high-emphasis">
        <h3 className="text-2xl font-bold tracking-tight">Magic URL</h3>
        <div className="flex items-center">
          <Button
            size="sm"
            variant="default"
            className="gap-1"
            onClick={() => setIsShortenDialogOpen(true)}
          >
            <Plus className="h-5 w-5" />
            <span className="sr-only sm:not-sr-only">Create Magic URL</span>
          </Button>
        </div>
      </div>
      <MagicUrlDialog
        open={isShortenDialogOpen}
        onOpenChange={setIsShortenDialogOpen}
      />
      <MagicUrls />
    </div>
  );
}
