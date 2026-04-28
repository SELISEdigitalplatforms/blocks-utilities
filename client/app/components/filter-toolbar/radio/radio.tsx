
import { PlusCircledIcon } from "@radix-ui/react-icons";
import usePopoverWidth from "@/hooks/use-popover-width";
import useIsMobile from "@/hooks/use-is-mobile";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { Button } from "@/components/ui-kits/button/button";
import { Separator } from "@/components/ui-kits/separator/separator";
import { Badge } from "@/components/ui-kits/badge/badge";
import { useMemo, useState } from "react";
import { RadioGroup, RadioGroupItem } from "@/components/ui-kits/radio-group/radio-group";
import { Label } from "@/components/ui-kits/label/label";
import { Search } from "lucide-react";
import { Input } from "@/components/ui-kits/input/input";
import { ClearButton } from "../clear-button/clear-button";

interface MultiSelectProps {
  label?: string;
  options: {
    label: string;
    value: string;
  }[];
  value: string;
  onChange: (value: unknown) => void;
}

export function Radio({ label, options, onChange, value }: MultiSelectProps) {
  const [search, setSearch] = useState("");
  const [buttonRef, popoverWidth] = usePopoverWidth();
  const isMobile = useIsMobile();

  const selected = useMemo(
    () => options.find((item) => item.value === value) || null,
    [options, value],
  );

  const searchedOptions = useMemo(() => {
    if (!search) return options;
    return options.filter((item) => item.label.toLowerCase().includes(search.toLowerCase()));
  }, [options, search]);

  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button ref={buttonRef} variant="outline" size="sm" className="h-8 border-dashed">
          <div className="flex w-full items-center justify-between">
            <div className="flex items-center">
              <PlusCircledIcon className="mr-2 h-4 w-4" />
              <span className="hidden sm:inline">{label}</span>
              <span className="sm:hidden">{label?.split(" ")[0]}</span>
            </div>
            {selected && (
              <>
                <Separator orientation="vertical" className="hidden h-4 sm:mx-2 sm:block" />
                <div className="flex space-x-1">
                  <Badge
                    variant="secondary"
                    key={selected.value}
                    className="rounded-sm px-1 font-normal"
                  >
                    {selected.label}
                  </Badge>
                </div>
              </>
            )}
          </div>
        </Button>
      </PopoverTrigger>
      <PopoverContent
        className="w-auto p-0 sm:w-full"
        align="start"
        style={isMobile ? { width: popoverWidth ? `${popoverWidth}px` : "auto" } : undefined}
      >
        <div className="flex items-center border-b px-3" cmdk-input-wrapper="">
          <Search className="mr-2 h-4 w-4 shrink-0 opacity-50" />
          <Input
            className={
              "flex h-11 w-full rounded-md border-none bg-transparent px-0 py-3 text-sm outline-none placeholder:text-muted-foreground focus-visible:ring-0 focus-visible:ring-offset-0 disabled:cursor-not-allowed disabled:opacity-50"
            }
            placeholder={label}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <>
          {searchedOptions.length > 0 ? (
            <RadioGroup
              onValueChange={onChange}
              value={value}
              className="gap-0 p-1 text-xs font-thin text-accent-foreground"
            >
              {searchedOptions.length > 0 &&
                searchedOptions.map((option) => (
                  <Label
                    key={option.value}
                    className="flex items-center gap-2 rounded-sm px-2 py-2 hover:bg-accent"
                    htmlFor={option.value}
                  >
                    <RadioGroupItem value={option.value} id={option.value} />
                    <span>{option.label}</span>
                  </Label>
                ))}
            </RadioGroup>
          ) : (
            <div className="py-6 text-center text-sm !text-popover-foreground">
              No results found.
            </div>
          )}
        </>

        {value ? (
          <>
            <Separator />
            <ClearButton onClear={() => onChange(null)} />
          </>
        ) : (
          ""
        )}
      </PopoverContent>
    </Popover>
  );
}
