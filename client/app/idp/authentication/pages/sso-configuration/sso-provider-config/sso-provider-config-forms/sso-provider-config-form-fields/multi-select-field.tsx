import React from "react";
import { FormItem, FormLabel, FormMessage } from "@/components/ui-kits/form/form";
import { ControllerRenderProps, FieldValues } from "react-hook-form";
import { SSOProviderConfigFormFieldType } from "../../sso-provider-config.type";
import { MultiSelectDropdown } from "./multi-select-dropdown";

type MultiSelectFieldProps = {
  item: Extract<SSOProviderConfigFormFieldType, { type: "multi-select" }>;
  field: ControllerRenderProps<FieldValues>;
};

export const MultiSelectField: React.FC<MultiSelectFieldProps> = ({ item, field }) => {
  const selectedValues = Array.isArray(field.value) ? field.value : [];

  return (
    <FormItem>
      <FormLabel>{item.label}</FormLabel>
      <MultiSelectDropdown
        disabled={item.isDisabled}
        options={item.options}
        placeholder={item.placeholder ?? `Select ${item.label}`}
        value={selectedValues}
        onChange={field.onChange}
      />
      {item.description && <div className="mt-2 text-sm text-low-emphasis">{item.description}</div>}
      <FormMessage />
    </FormItem>
  );
};
