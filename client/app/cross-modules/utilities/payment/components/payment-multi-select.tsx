import { useState } from "react";
import { ChevronDown, Plus } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Input } from "@/components/ui-kits/input/input";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui-kits/popover/popover";

interface PaymentMultiSelectProps {
  label: string;
  values: string[];
  options: readonly string[];
  emptyLabel: string;
  onChange: (values: string[]) => void;
  allowCustomValue?: boolean;
}

export const PaymentMultiSelect = ({
  label,
  values,
  options,
  emptyLabel,
  onChange,
  allowCustomValue = false,
}: PaymentMultiSelectProps) => {
  const [customValue, setCustomValue] = useState("");

  const toggleValue = (value: string) => {
    onChange(
      values.includes(value)
        ? values.filter((current) => current !== value)
        : [...values, value],
    );
  };

  const addCustomValue = () => {
    const normalized = customValue.trim().toUpperCase();

    if (!normalized || values.includes(normalized) || values.length >= 20) {
      return;
    }

    onChange([...values, normalized]);
    setCustomValue("");
  };

  const displayedOptions = Array.from(new Set([...options, ...values]));

  return (
    <div className="space-y-1.5">
      <label className="text-xs font-medium text-muted-foreground">
        {label}
      </label>
      <Popover>
        <PopoverTrigger asChild>
          <Button
            type="button"
            variant="outline"
            className="w-full justify-between px-3 font-normal"
          >
            <span className="truncate">
              {values.length === 0
                ? emptyLabel
                : values.length === 1
                  ? values[0]
                  : `${values.length} selected`}
            </span>
            <ChevronDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        </PopoverTrigger>
        <PopoverContent align="start" className="w-80 space-y-3 p-3">
          <div className="max-h-64 space-y-1 overflow-y-auto">
            {displayedOptions.map((option) => {
              const selected = values.includes(option);

              const optionId = `payment-filter-${label}-${option}`
                .toLowerCase()
                .replace(/[^a-z0-9]+/g, "-");

              return (
                <div
                  key={option}
                  className="flex items-center gap-3 rounded-md px-2 py-2 text-sm hover:bg-muted"
                >
                  <Checkbox
                    id={optionId}
                    checked={selected}
                    onCheckedChange={() => toggleValue(option)}
                    aria-label={`Select ${option}`}
                  />
                  <label
                    htmlFor={optionId}
                    className="min-w-0 flex-1 cursor-pointer truncate"
                  >
                    {option}
                  </label>
                </div>
              );
            })}
          </div>

          {allowCustomValue && (
            <div className="flex gap-2 border-t pt-3">
              <Input
                value={customValue}
                maxLength={100}
                placeholder="Add provider name"
                onChange={(event) => setCustomValue(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === "Enter") {
                    event.preventDefault();
                    addCustomValue();
                  }
                }}
              />
              <Button
                type="button"
                size="icon"
                variant="outline"
                aria-label="Add provider"
                onClick={addCustomValue}
              >
                <Plus className="h-4 w-4" />
              </Button>
            </div>
          )}
        </PopoverContent>
      </Popover>
    </div>
  );
};
