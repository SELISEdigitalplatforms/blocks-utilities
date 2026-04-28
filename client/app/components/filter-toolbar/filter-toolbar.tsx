import { ReactNode, useRef } from "react";
import { FilterControls } from ".";
import { ResetButton } from "./reset-button/reset-button";
import {
  Sheet,
  SheetClose,
  SheetContent,
  SheetDescription,
  SheetTitle,
  SheetTrigger,
} from "../ui-kits/sheet/sheet";
import { Button } from "../ui-kits/button/button";
import { Filter } from "lucide-react";
import { deepEqual } from "@/lib/utils";

type FilterItem<T extends Record<string, unknown>> = {
  [K in keyof typeof FilterControls]: {
    key: keyof T;
    type: K;
    span?: number;
    label: string;
    props?: Omit<React.ComponentProps<(typeof FilterControls)[K]>, "value" | "onChange" | "label">;
  };
}[keyof typeof FilterControls];

export type FilterChangeHandler<T extends Record<string, unknown>> = <K extends keyof T>(
  key: K,
  value: T[K],
  values: T,
) => void;

type FilterToolbarProps<T extends Record<string, unknown>> = {
  filters: FilterItem<T>[];
  values: T;
  defaultValues: T;
  onChange: FilterChangeHandler<T>;
  onReset?: (values?: T) => void;
  hideGlobalResetButton?: boolean;
};

type ViewType = {
  Components: ReactNode[];
  onReset?: () => void;
  showReset: boolean;
};

const FilterToolbarDesktopView = ({ Components, showReset, onReset }: ViewType) => {
  return (
    <div className="hidden flex-wrap items-center gap-4 md:flex">
      {Components.map((item) => item)}
      {showReset && (
        <ResetButton
          onClick={() => {
            if (onReset) onReset();
          }}
        />
      )}
    </div>
  );
};

export const FilterToolBarMobileView = ({ Components, showReset, onReset }: ViewType) => {
  return (
    <div className={"flex items-center justify-between gap-2 md:hidden"}>
      <div className="min-w-0 max-w-72 flex-1">{Components[0]}</div>
      {Components.length > 1 && (
        <Sheet>
          <SheetTrigger asChild>
            <Button variant="outline" size="sm" className="relative h-8 w-8 p-0">
              <Filter className="h-4 w-4" />
              {/* {activeFilter > 0 && (
              <Badge className="absolute -right-2 -top-2 h-4 w-4 px-1 text-xs font-medium">
                {activeFilter}
              </Badge>
            )} */}
            </Button>
          </SheetTrigger>
          <SheetContent side="right" className="w-full" aria-describedby="filter-description">
            <SheetTitle className="mb-4">Filter</SheetTitle>
            <SheetDescription></SheetDescription>
            <div className="flex flex-col space-y-4">
              {Components.slice(1).map((item) => item)}
              <SheetClose asChild>
                <Button className="mt-4" size="sm">
                  Show Results
                </Button>
              </SheetClose>

              {showReset && (
                <ResetButton
                  onClick={() => {
                    if (onReset) onReset();
                  }}
                />
              )}
            </div>
          </SheetContent>
        </Sheet>
      )}
    </div>
  );
};

export const FilterToolbar = <T extends Record<string, unknown>>({
  filters,
  values,
  onChange,
  onReset,
  defaultValues,
  hideGlobalResetButton = false,
}: FilterToolbarProps<T>) => {
  const initialValuesRef = useRef(defaultValues);

  const changeHandler = <K extends keyof T>(key: K, value: T[K]) => {
    const changedValues = { ...values, [key]: value };
    onChange(key, value, changedValues);
  };

  const controllers = filters.map((item) => {
    const Component = FilterControls[item.type];

    return (
      <Component
        {...item}
        // eslint-disable-next-line @typescript-eslint/ban-ts-comment
        //@ts-expect-error
        value={values[item.key]}
        onChange={(val: unknown) => changeHandler(item.key as keyof T, val as T[keyof T])}
        {...item.props}
        key={String(item.key)}
      />
    );
  });

  const showReset = !hideGlobalResetButton && !deepEqual(initialValuesRef.current, values);

  return (
    <>
      <FilterToolbarDesktopView
        Components={controllers}
        showReset={showReset}
        onReset={() => onReset && onReset(initialValuesRef.current)}
      />
      <FilterToolBarMobileView
        Components={controllers}
        showReset={showReset}
        onReset={() => onReset && onReset(initialValuesRef.current)}
      />
    </>
  );
};
