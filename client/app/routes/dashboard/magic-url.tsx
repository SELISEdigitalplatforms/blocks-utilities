import { MagicUrls } from "@blocks-utilities/pages/magic-urls/magic-urls";
import { Button } from "@/components/ui-kits/button/button";
import { Plus, Settings } from "lucide-react";
import { useState } from "react";
import { MagicUrlDialog } from "@blocks-utilities/components/magic-url-dialog/magic-url-dialog";
import { MagicUrlConfigDialog } from "@blocks-utilities/components/magic-url-config-dialog/magic-url-config-dialog";
import { useProjectStore } from "@/store/useProjectStore";
import { useSaveMagicUrlConfig } from "@blocks-utilities/hooks/use-magic-url";

export default function MagicUrlPage() {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  const [isShortenDialogOpen, setIsShortenDialogOpen] = useState(false);
  const [isConfigDialogOpen, setIsConfigDialogOpen] = useState(false);
  const { mutateAsync: saveMagicUrlConfig } = useSaveMagicUrlConfig();

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex w-full justify-between text-high-emphasis">
        <h3 className="text-2xl font-bold tracking-tight">Magic URL</h3>
        <div className="flex items-center gap-2">
          <Button
            size="sm"
            variant="default"
            className="gap-1"
            onClick={() => setIsShortenDialogOpen(true)}
          >
            <Plus className="h-5 w-5" />
            <span className="sr-only sm:not-sr-only">Create Magic URL</span>
          </Button>
          <Button
            size="sm"
            variant="outline"
            className="gap-1"
            onClick={() => setIsConfigDialogOpen(true)}
          >
            <Settings className="h-5 w-5" />
            <span className="sr-only sm:not-sr-only">Configure</span>
          </Button>
        </div>
      </div>
      <MagicUrlDialog
        open={isShortenDialogOpen}
        onOpenChange={setIsShortenDialogOpen}
      />
      <MagicUrlConfigDialog
        open={isConfigDialogOpen}
        onOpenChange={setIsConfigDialogOpen}
        projectKey={tenantId}
        onSave={async (config) => {
          await saveMagicUrlConfig(config);
        }}
      />
      <MagicUrls />
    </div>
  );
}
