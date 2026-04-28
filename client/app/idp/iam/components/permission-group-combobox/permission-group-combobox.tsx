import { Button } from "@/components/ui-kits/button/button";
import { Command, CommandInput, CommandItem, CommandList } from "@/components/ui-kits/command/command";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { useProjectStore } from "@/store/useProjectStore";
import { useGetResourceGroup } from "@blocks-idp/iam/hooks/use-permission";
import { Plus } from "lucide-react";
import { useMemo, useState } from "react";

type PermissionGroupComboboxProps = {
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
};

export function PermissionGroupCombobox({ value, onChange, disabled }: PermissionGroupComboboxProps) {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data: resourceGroupData } = useGetResourceGroup({ projectKey: tenantId });
  const [open, setOpen] = useState(false);
  const [inputValue, setInputValue] = useState("");

  const handleSelect = (value: string) => {
    setOpen(false);
    onChange(value);
  };

  const filtered = useMemo(() => {
    const data = resourceGroupData?.map((item) => item.resourceGroup) || [];
    if (!data) return [];
    if (!inputValue) return data;
    return data.filter((item) => item.toLowerCase().includes(inputValue.toLowerCase().trim()));
  }, [resourceGroupData, inputValue]);

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild disabled={disabled}>
        <Button variant="outline" className="flex w-full justify-between">
          {value || "Select or type..."}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-[--radix-popover-trigger-width] p-0">
        <Command shouldFilter={false}>
          <div className="relative">
            <CommandInput
              placeholder="Search or type..."
              value={inputValue}
              onValueChange={(v) => {
                setInputValue(v);
              }}
              // onKeyDown={(e) => {
              //   if (e.key !== "Enter") return;
              // }}
            />
            <Button
              className="absolute right-1 top-1.5 h-8 w-8 rounded-full p-0.5"
              size="icon"
              variant="ghost"
              onClick={() => {
                handleSelect(inputValue);
              }}
            >
              <Plus className="aspect-square w-4" />
            </Button>
          </div>
          <CommandList>
            {filtered.length > 0 &&
              filtered.map((opt) => (
                <CommandItem key={opt} value={opt} onSelect={() => handleSelect(opt)}>
                  {opt}
                </CommandItem>
              ))}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
