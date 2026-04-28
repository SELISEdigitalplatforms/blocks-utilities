import { ReactNode } from "react";

export type SSOProviderConfigFormFieldBase = {
  id: string;
  label: string;
  name: string;
  description?: ReactNode;
  isDisabled?: boolean;
};

export type SSOProviderConfigSelectOption = {
  label: string;
  value: string;
};

export type SSOProviderConfigFormFieldType = SSOProviderConfigFormFieldBase &
  (
    | { type: "input" }
    | { type: "password" }
    | { type: "select"; options: SSOProviderConfigSelectOption[]; placeholder?: string }
    | { type: "multi-select"; options: SSOProviderConfigSelectOption[]; placeholder?: string }
    | { type: "radio"; options: SSOProviderConfigSelectOption[] }
  );

export type SSOProviderConfigForm = {
  fields: SSOProviderConfigFormFieldType[];
};
