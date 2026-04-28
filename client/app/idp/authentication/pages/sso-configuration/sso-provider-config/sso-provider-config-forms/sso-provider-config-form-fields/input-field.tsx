import React from "react";
import { Input } from "@/components/ui-kits/input/input";
import { FormControl, FormItem, FormLabel, FormMessage } from "@/components/ui-kits/form/form";
import { ControllerRenderProps, FieldValues } from "react-hook-form";
import { SSOProviderConfigFormFieldType } from "../../sso-provider-config.type";

type InputFieldProps = {
  item: Extract<SSOProviderConfigFormFieldType, { type: "input" }>;
  field: ControllerRenderProps<FieldValues>;
};

export const InputField: React.FC<InputFieldProps> = ({ item, field }) => {
  return (
    <FormItem>
      <FormLabel>{item.label}</FormLabel>
      <FormControl>
        <Input {...field} disabled={item.isDisabled} />
      </FormControl>
      {item.description && <div className="mt-2 text-sm text-low-emphasis">{item.description}</div>}
      <FormMessage />
    </FormItem>
  );
};
