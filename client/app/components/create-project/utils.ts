import { create } from "zustand";
import { createProjectNamingFormDefaultValue } from "./form/create-project-naming-form/utils";
import { createProjectEnvironmentFormDefaultValue } from "./form/create-project-environments-form/utils";
import { CreateProjectResourcesFormDefaultValue } from "./form/create-project-resources-form/utils";

interface ICreateProjectFormState {
  formData: [
    typeof createProjectNamingFormDefaultValue,
    typeof CreateProjectResourcesFormDefaultValue,
    typeof createProjectEnvironmentFormDefaultValue,
  ];
  setFormData: (
    index: number,
    data:
      | typeof createProjectNamingFormDefaultValue
      | typeof CreateProjectResourcesFormDefaultValue
      | typeof createProjectEnvironmentFormDefaultValue,
  ) => void;
  resetFormData: () => void;
}

export const useCreateProjectFormState = create<ICreateProjectFormState>((set, get) => ({
  formData: [
    createProjectNamingFormDefaultValue,
    CreateProjectResourcesFormDefaultValue,
    createProjectEnvironmentFormDefaultValue,
  ],
  setFormData: (index, data) => {
    const state = get();
    const { formData } = state;
    formData[index] = data;
    set(() => ({ ...state }));
  },
  resetFormData: () => {
    set((state) => ({
      ...state,
      formData: [
        createProjectNamingFormDefaultValue,
        CreateProjectResourcesFormDefaultValue,
        createProjectEnvironmentFormDefaultValue,
      ],
    }));
  },
}));

export const shortGuidGenerator = (length: number): string => {
  const letters = "abcdefghijklmnopqrstuvwxyz";
  const bytes = crypto.getRandomValues(new Uint8Array(length));
  return Array.from(bytes, (b) => letters[b % letters.length]).join("");
};
