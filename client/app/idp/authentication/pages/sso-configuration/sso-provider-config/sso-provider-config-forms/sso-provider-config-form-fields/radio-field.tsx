import React from "react";
import { FormControl, FormItem, FormLabel, FormMessage } from "@/components/ui-kits/form/form";
import { Label } from "@/components/ui-kits/label/label";
import { RadioGroup, RadioGroupItem } from "@/components/ui-kits/radio-group/radio-group";
import { cn } from "@/lib/utils";
import { ControllerRenderProps, FieldValues } from "react-hook-form";
import { SSOProviderConfigFormFieldType } from "../../sso-provider-config.type";

type RadioFieldProps = {
  item: Extract<SSOProviderConfigFormFieldType, { type: "radio" }>;
  field: ControllerRenderProps<FieldValues>;
};

export const RadioField: React.FC<RadioFieldProps> = ({ item, field }) => {
  const radioValue = typeof field.value === "string" ? field.value : "";

  return (
    <FormItem>
      <FormLabel>{item.label}</FormLabel>
      <FormControl>
        <RadioGroup
          value={radioValue}
          onValueChange={(value) => {
            if (item.isDisabled) return;
            field.onChange(value);
          }}
          onBlur={field.onBlur}
          className="flex items-center gap-4"
        >
          {item.options.map((option) => {
            const optionId = `${item.name}-${option.value}`;

            return (
              <div key={option.value} className="flex items-center space-x-2">
                <RadioGroupItem id={optionId} value={option.value} disabled={item.isDisabled} />
                <Label
                  htmlFor={optionId}
                  className={cn(
                    "cursor-pointer text-sm font-medium",
                    item.isDisabled ? "cursor-not-allowed text-muted-foreground" : undefined,
                  )}
                >
                  {option.label}
                </Label>
              </div>
            );
          })}
        </RadioGroup>
      </FormControl>
      {item.description && <div className="mt-2 text-sm text-low-emphasis">{item.description}</div>}
      <FormMessage />
    </FormItem>
  );
};
