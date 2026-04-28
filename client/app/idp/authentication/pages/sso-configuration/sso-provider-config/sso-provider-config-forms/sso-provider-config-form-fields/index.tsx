import { ControllerRenderProps, FieldValues, Path, UseFormReturn } from "react-hook-form";
import { FormField } from "@/components/ui-kits/form/form";
import { SSOProviderConfigFormFieldType } from "../../sso-provider-config.type";
import { SelectField } from "./select-field";
import { MultiSelectField } from "./multi-select-field";
import { RadioField } from "./radio-field";
import { PasswordField } from "./password-field";
import { InputField } from "./input-field";

type SSOProviderConfigFormFieldProps<T extends FieldValues> = {
  fields: SSOProviderConfigFormFieldType[];
  form: UseFormReturn<T>;
};

export const SSOProviderConfigFormField = <T extends FieldValues>({
  fields,
  form,
}: SSOProviderConfigFormFieldProps<T>) => {
  return (
    <>
      {fields.map((item) => (
        <FormField
          key={item.id}
          control={form.control}
          name={item.name as Path<T>}
          render={({ field }) => {
            const f = field as ControllerRenderProps<FieldValues>;
            switch (item.type) {
              case "select":
                return <SelectField item={item} field={f} />;
              case "multi-select":
                return <MultiSelectField item={item} field={f} />;
              case "radio":
                return <RadioField item={item} field={f} />;
              case "password":
                return <PasswordField item={item} field={f} />;
              default:
                return <InputField item={item} field={f} />;
            }
          }}
        />
      ))}
    </>
  );
};
