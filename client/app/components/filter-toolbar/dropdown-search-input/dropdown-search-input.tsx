import { useRef, MouseEvent, useState, useEffect, ReactNode } from "react";
import { X } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { cn, debounce } from "@/lib/utils";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";

type ValueType = { selected: string; value: string };

interface DropdownSearchInputProps {
  onChange: (params: ValueType) => void;
  placeholder?: string;
  value: ValueType;
  className?: {
    selectContent?: string;
    SelectItem?: string;
    input?: string;
  };
  options: { label: ReactNode; value: string }[];
}

export const DropdownSearchInput: React.FC<DropdownSearchInputProps> = ({
  onChange,
  placeholder = "Search...",
  value,
  className = {},
  options = [],
}) => {
  const [state, setState] = useState<ValueType>(value);
  const [prevValue, setPrevValue] = useState<ValueType>(value);
  const inputRef = useRef<HTMLInputElement>(null);

  if (prevValue !== value) {
    setPrevValue(value);
    setState(value);
  }

  const debouncedRef = useRef(
    debounce((val: ValueType) => {
      onChange(val);
    }, 300),
  );

  useEffect(() => {
    const debounced = debouncedRef.current;
    return () => {
      debounced.cancel();
    };
  }, []);

  const handleChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    event.stopPropagation();
    const data = { ...state, value: event.target.value };
    setState(data);
    debouncedRef.current(data);
  };

  const handleClear = (e: MouseEvent) => {
    e.stopPropagation();
    const data = { ...state, value: "" };
    setState(data);
    onChange(data);
  };

  const handleSelect = (value: string) => {
    setState({ selected: value, value: "" });
    onChange({ selected: value, value: "" });
  };

  return (
    <div className="flex items-center gap-2 rounded-md border pr-2">
      <Select onValueChange={handleSelect} value={state.selected}>
        <SelectTrigger className="h-8 w-fit gap-1 rounded-e-none border-0 border-r focus:ring-0 focus:ring-ring focus:ring-offset-0">
          <SelectValue></SelectValue>
        </SelectTrigger>
        <SelectContent className={cn(className.selectContent)}>
          {options.map((item) => (
            <SelectItem key={item.value} value={item.value} className={cn(className.SelectItem)}>
              {item.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      <Input
        ref={inputRef}
        placeholder={placeholder}
        value={state.value}
        onChange={handleChange}
        className={cn(
          "h-8 w-52 border-none p-0 focus-visible:ring-0 focus-visible:ring-offset-0",
          className?.input,
        )}
      />

      <Button
        variant="ghost"
        size="xs"
        className={cn("h-full p-1 pr-0 hover:bg-transparent", !value.value && "invisible")}
        onClick={handleClear}
      >
        <X className="h-4 w-4 text-muted-foreground" />
      </Button>
    </div>
  );
};
