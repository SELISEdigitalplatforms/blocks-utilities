import { useForm } from "react-hook-form";
import { GitBranch } from "lucide-react";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import {
  createProjectEnvironmentFormDefaultValue,
  createProjectEnvironmentFormSchema,
  getEnvironmentOptions,
} from "./utils";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui-kits/button/button";
import { useCreateProjectFormState } from "../../utils";
import { useProjectForm } from "@blocks-identifier/hooks/use-project";
import { useT } from "@seliseblocks/genesis-os";

export const CreateProjectEnvironmentsForm = () => {
  const { t } = useT("utilities.shared");
  const { isPending, saveProject } = useProjectForm();
  const { formData, setFormData } = useCreateProjectFormState();
  const form = useForm({
    defaultValues: formData[2],
    resolver: zodResolver(createProjectEnvironmentFormSchema()),
  });

  const onSubmitHandler = (values: typeof createProjectEnvironmentFormDefaultValue) => {
    const sortedEnvironments = [...values.environments].sort(
      (a: { value: string }, b: { value: string }) => {
        const aIndex = getEnvironmentOptions().find((opt) => opt.value === a.value)?.index ?? 0;
        const bIndex = getEnvironmentOptions().find((opt) => opt.value === b.value)?.index ?? 0;
        return aIndex - bIndex;
      },
    );
    setFormData(2, { ...values, environments: sortedEnvironments });
    saveProject();
  };

  const { isValid } = form.formState;

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(onSubmitHandler)}>
        <div className="mt-4 flex flex-col gap-1 text-left">
          <p className="text-3xl font-bold tracking-tight">{t("CREATE_PROJECT_ENVIRONMENTS_FORM.SELECT_ENVIRONMENTS")}</p>
          <p className="text-base font-normal tracking-tight">
            {t("CREATE_PROJECT_ENVIRONMENTS_FORM.SELECT_THE_ENVIRONMENTS_YOU_WANT")}
          </p>
          <div className="mt-2 flex min-h-10 w-fit flex-row items-center gap-1 rounded border border-base-warning bg-warning-100 p-3 text-sm text-warning-800">
            <span>
              {t("CREATE_PROJECT_ENVIRONMENTS_FORM.PLEASE_ENSURE_THAT_THE_BRANCH")}
            </span>
          </div>
          <div className="">
            <div className="mt-8 text-sm">
              <FormField
                control={form.control}
                name="environments"
                render={() => (
                  <FormItem>
                    {getEnvironmentOptions().map((option) => (
                      <FormField
                        key={option.value}
                        control={form.control}
                        name="environments"
                        render={({ field }) => {
                          const isSelected = field.value?.some(
                            (env: { value: string }) => env.value === option.value,
                          );
                          return (
                            <FormItem className="mb-4 flex flex-col">
                              <div className="flex items-center gap-2">
                                <FormControl>
                                  <Checkbox
                                    className="h-5 w-5"
                                    checked={isSelected}
                                    onCheckedChange={(checked) => {
                                      const currentValues = [...(field.value || [])];
                                      if (checked) {
                                        field.onChange([...currentValues, { value: option.value }]);
                                      } else {
                                        field.onChange(
                                          currentValues.filter(
                                            (env: { value: string }) => env.value !== option.value,
                                          ),
                                        );
                                      }
                                    }}
                                  />
                                </FormControl>
                                <FormLabel className="!m-0 text-lg font-bold">
                                  <div className="flex flex-row items-center gap-2">
                                    <span>{option.label}</span>
                                    <div className="flex flex-row items-center">
                                      <GitBranch className="h-3 w-3 text-gray-400" />
                                      <span className="text-sm text-gray-400">
                                        {option.value === "prod" ? "main" : option.value}
                                      </span>
                                    </div>
                                  </div>
                                </FormLabel>
                              </div>
                              <div className="ml-7 text-base font-normal">{option.subtext}</div>
                            </FormItem>
                          );
                        }}
                      />
                    ))}
                    <FormMessage />
                  </FormItem>
                )}
              />
            </div>
          </div>
        </div>
        <div className="mb-4 mt-10">
          <Button size="lg" disabled={!isValid || isPending}>
            {t("common:SUBMIT")}
          </Button>
        </div>
      </form>
    </Form>
  );
};
