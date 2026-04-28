import React from "react";
import { FormControl, FormItem, FormLabel, FormMessage } from "@/components/ui-kits/form/form";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { ControllerRenderProps, FieldValues } from "react-hook-form";
import { SSOProviderConfigFormFieldType } from "../../sso-provider-config.type";

type SelectFieldProps = {
  item: Extract<SSOProviderConfigFormFieldType, { type: "select" }>;
  field: ControllerRenderProps<FieldValues>;
};

export const SelectField: React.FC<SelectFieldProps> = ({ item, field }) => {
  const selectValue = typeof field.value === "string" ? field.value : undefined;

  return (
    <FormItem>
      <FormLabel>{item.label}</FormLabel>
      <Select disabled={item.isDisabled} onValueChange={field.onChange} value={selectValue}>
        <FormControl>
          <SelectTrigger disabled={item.isDisabled}>
            <SelectValue placeholder={item.placeholder ?? `Select ${item.label}`} />
          </SelectTrigger>
        </FormControl>
        <SelectContent>
          {item.options.map((option) => (
            <SelectItem key={option.value} value={option.value}>
              {option.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      {item.description && <div className="mt-2 text-sm text-low-emphasis">{item.description}</div>}
      <FormMessage />
    </FormItem>
  );
};
