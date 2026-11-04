import { useState } from "react";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Button } from "@/components/ui-kits/button/button";
import { useCreateProject } from "@blocks-identifier/hooks/use-project";
import { getEnvironmentOptions } from "@/constants/environment-options";
import { useT } from "@seliseblocks/genesis-os";

function shortGuidGenerator(length: number): string {
  const letters = "abcdefghijklmnopqrstuvwxyz";
  const bytes = crypto.getRandomValues(new Uint8Array(length));
  return Array.from(bytes, (b) => letters[b % letters.length]).join("");
}

interface AddEnvironmentModalProps {
  onClose?: (selectedEnvironments: string[]) => void | Promise<void>;
  preSelectedEnvironments?: string[];
  tenantGroupId?: string;
  projectName?: string;
}

export const AddEnvironmentModal = ({
  onClose,
  preSelectedEnvironments = [],
  tenantGroupId,
  projectName,
}: AddEnvironmentModalProps) => {
  const { t } = useT("utilities.shared");
  const { isPending, mutateAsync } = useCreateProject();
  const [selected, setSelected] = useState<string[]>([]);

  const availableOptions = getEnvironmentOptions().filter(
    (option) => !preSelectedEnvironments.includes(option.value),
  );

  const rows = [];
  for (let i = 0; i < availableOptions.length; i += 2) {
    rows.push(availableOptions.slice(i, i + 2));
  }

  const onSaveClick = () => {
    if (selected.length > 0 && onClose && tenantGroupId) {
      const sortedSelected = [...selected].sort((a, b) => {
        const aIndex = getEnvironmentOptions().find((opt) => opt.value === a)?.index ?? 0;
        const bIndex = getEnvironmentOptions().find((opt) => opt.value === b)?.index ?? 0;
        return aIndex - bIndex;
      });
      const domain = import.meta.env.VITE_BASE_DOMAIN || "seliseblocks.com";
      const shortGuid = shortGuidGenerator(5);
      const applicationContexts = sortedSelected.map((env: string) => ({
        environment: env,
        domain: `https://${env === "main" ? "" : env}-${shortGuid}.${domain}`,
        cookieDomain: domain,
      }));
      mutateAsync({
        name: projectName || "old Project",
        isAcceptBlocksTerms: true,
        isUseBlocksExclusively: true,
        isProduction: false,
        resources: [],
        tenantGroupId: tenantGroupId || "default-tenant-group-id",
        applicationContexts: applicationContexts,
      });

      onClose(sortedSelected);
    }
  };

  return (
    <div>
      <div className="grid">
        {rows.map((row, rowIdx) => (
          <div className="grid grid-cols-2 gap-4" key={rowIdx}>
            {row.map((option) => {
              const isChecked = selected.includes(option.value);
              return (
                <div key={option.value} className="flex flex-col rounded p-3">
                  <div className="flex items-center gap-2">
                    <Checkbox
                      className="h-5 w-5"
                      checked={isChecked}
                      onCheckedChange={(checked) => {
                        setSelected((prev) => {
                          if (checked) {
                            return [...prev, option.value];
                          } else {
                            return prev.filter((v) => v !== option.value);
                          }
                        });
                      }}
                    />
                    <span className="text-sm">{option.label}</span>
                  </div>
                </div>
              );
            })}
          </div>
        ))}
      </div>
      <div className="mt-6 flex justify-end gap-2">
        <Button
          type="button"
          variant="outline"
          disabled={isPending}
          onClick={() => onClose && onClose([])}
        >
          {t("common:CANCEL")}
        </Button>
        <Button
          type="button"
          disabled={isPending || selected.length === 0}
          onClick={() => onSaveClick()}
        >
          {t("common:ADD")}
        </Button>
      </div>
    </div>
  );
};
