import React, { useMemo, useState } from "react";
import { FormControl } from "@/components/ui-kits/form/form";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandSeparator,
} from "@/components/ui-kits/command/command";
import { Check, ChevronDown } from "lucide-react";
import { cn } from "@/lib/utils";

type MultiSelectDropdownProps = {
  disabled?: boolean;
  options: { label: string; value: string }[];
  placeholder?: string;
  value: string[];
  onChange: (values: string[]) => void;
};

export const MultiSelectDropdown: React.FC<MultiSelectDropdownProps> = ({
  disabled,
  options,
  placeholder,
  value,
  onChange,
}) => {
  const [open, setOpen] = useState(false);

  const selectedLabels = useMemo(
    () => options.filter((option) => value.includes(option.value)).map((option) => option.label),
    [options, value],
  );

  const handleToggle = (optionValue: string) => {
    const isSelected = value.includes(optionValue);
    const next = isSelected
      ? value.filter((item) => item !== optionValue)
      : [...value, optionValue];
    const ordered = options.map((option) => option.value).filter((option) => next.includes(option));
    onChange(ordered);
  };

  const handleClear = () => {
    onChange([]);
  };

  const displayText = selectedLabels.join(", ");
  const fallbackPlaceholder = placeholder || "Select options";

  return (
    <Popover open={open} onOpenChange={(nextOpen) => !disabled && setOpen(nextOpen)}>
      <PopoverTrigger asChild>
        <FormControl>
          <button
            type="button"
            disabled={disabled}
            className={cn(
              "flex h-10 w-full items-center justify-between rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50",
            )}
          >
            <span
              className={cn(
                "flex-1 truncate text-left",
                displayText ? undefined : "text-muted-foreground",
              )}
            >
              {displayText || fallbackPlaceholder}
            </span>
            <ChevronDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </button>
        </FormControl>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[--radix-popover-trigger-width] p-0">
        <Command>
          {options.length > 7 && <CommandInput placeholder="Search options..." className="h-9" />}
          <CommandList>
            <CommandEmpty>No results found.</CommandEmpty>
            <CommandGroup>
              {options.map((option) => {
                const isSelected = value.includes(option.value);

                return (
                  <CommandItem
                    key={option.value}
                    value={option.label}
                    onSelect={() => handleToggle(option.value)}
                  >
                    <span
                      className={cn(
                        "mr-2 flex h-4 w-4 items-center justify-center rounded-sm border border-primary",
                        isSelected
                          ? "bg-primary text-primary-foreground"
                          : "opacity-50 [&_svg]:invisible",
                      )}
                    >
                      <Check className="h-4 w-4" />
                    </span>
                    <span className="whitespace-nowrap text-sm">{option.label}</span>
                  </CommandItem>
                );
              })}
            </CommandGroup>
            {value.length > 0 && (
              <>
                <CommandSeparator />
                <CommandGroup>
                  <CommandItem className="justify-center text-sm" onSelect={handleClear}>
                    Clear selection
                  </CommandItem>
                </CommandGroup>
              </>
            )}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
};
