import React from "react";
import { FormControl, FormItem, FormLabel, FormMessage } from "@/components/ui-kits/form/form";
import { ControllerRenderProps, FieldValues } from "react-hook-form";
import { SSOProviderConfigFormFieldType } from "../../sso-provider-config.type";
import { PasswordInput } from "@/components/password-input";

type PasswordFieldProps = {
  item: Extract<SSOProviderConfigFormFieldType, { type: "password" }>;
  field: ControllerRenderProps<FieldValues>;
};

export const PasswordField: React.FC<PasswordFieldProps> = ({ item, field }) => {
  return (
    <FormItem>
      <FormLabel>{item.label}</FormLabel>
      <FormControl>
        <PasswordInput {...field} disabled={item.isDisabled} />
      </FormControl>
      {item.description && <div className="mt-2 text-sm text-low-emphasis">{item.description}</div>}
      <FormMessage />
    </FormItem>
  );
};
