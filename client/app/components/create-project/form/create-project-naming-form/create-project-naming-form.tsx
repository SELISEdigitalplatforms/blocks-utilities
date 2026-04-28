import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { createProjectNamingFormDefaultValue, createProjectNamingFormSchema } from "./utils";
import { Button } from "@/components/ui-kits/button/button";
import { useStepper } from "@/components/stepper/stepper-provider";
import { useCreateProjectFormState } from "../../utils";
import { Input } from "@/components/ui-kits/input/input";

export const CreateProjectNamingForm = () => {
  const { formData, setFormData } = useCreateProjectFormState();
  const { nextStep } = useStepper();
  const form = useForm({
    values: formData[0],
    resolver: zodResolver(createProjectNamingFormSchema),
  });

  const onSubmitHandler = (values: typeof createProjectNamingFormDefaultValue) => {
    setFormData(0, values);
    nextStep();
  };
  const { isValid } = form.formState;

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(onSubmitHandler)}>
        <div className="mt-4 flex flex-col gap-1 text-left">
          <h3 className="text-3xl font-semibold tracking-tight">Name your project</h3>
          <div className="mt-5 max-w-[437px] sm:mt-5">
            <FormField
              name="name"
              control={form.control}
              render={({ field }) => (
                <FormItem>
                  <FormControl>
                    <Input
                      {...field}
                      onChange={(e) =>
                        form.setValue("name", e.target.value, { shouldValidate: true })
                      }
                      type="text"
                      className="h-auto w-full !py-3 px-3 text-base"
                      placeholder="Enter your project name"
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <div className="mt-10">
              <FormField
                control={form.control}
                name="isUseBlocksExclusively"
                render={({ field }) => (
                  <FormItem>
                    <div className="flex gap-2">
                      <FormControl>
                        <Checkbox checked={field.value} onCheckedChange={field.onChange} />
                      </FormControl>
                      <FormLabel className="-mt-[2px] flex-1 text-sm font-medium text-black dark:text-white">
                        I confirm that I will use Blocks exclusively for purposes relating to my
                        trade, business, craft, or profession
                      </FormLabel>
                    </div>
                  </FormItem>
                )}
              />
            </div>
            <div className="mt-4">
              <FormField
                control={form.control}
                name="isAcceptBlocksTerms"
                render={({ field }) => (
                  <FormItem>
                    <div className="flex items-center gap-2">
                      <FormControl>
                        <Checkbox checked={field.value} onCheckedChange={field.onChange} />
                      </FormControl>
                      <FormLabel className="!m-0 text-sm font-medium text-black dark:text-white">
                        I accept the{" "}
                        <a
                          href="https://selisegroup.com/software-development-term/"
                          className="text-primary"
                          target="_blank"
                          rel="noopener noreferrer"
                        >
                          Terms of services
                        </a>
                      </FormLabel>
                    </div>
                  </FormItem>
                )}
              />
            </div>
          </div>
        </div>
        <div className="mt-10">
          <Button size="lg" disabled={!isValid}>
            Continue
          </Button>
        </div>
      </form>
    </Form>
  );
};
