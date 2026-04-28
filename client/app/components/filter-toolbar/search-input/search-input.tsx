import { useRef, MouseEvent, useState, useEffect } from "react";
import { Search, X } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { cn, debounce } from "@/lib/utils";

interface SearchInputProps {
  onChange: (value: string) => void;
  placeholder?: string;
  value: string;
  className?: string;
}

export const SearchInput: React.FC<SearchInputProps> = ({
  onChange,
  placeholder = "Search...",
  value,
  className = "",
}) => {
  const [state, setState] = useState(value);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    setState(value);
  }, [value]);

  const debounced = useRef(
    debounce((val: string) => {
      onChange(val);
    }, 300),
  ).current;

  useEffect(() => {
    return () => {
      debounced.cancel();
    };
  }, [debounced]);

  const handleChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    event.stopPropagation();
    setState(event.target.value);
    debounced(event.target.value);
  };

  const handleClear = (e: MouseEvent) => {
    e.stopPropagation();
    setState("");
    onChange("");
  };

  return (
    <div className="flex items-center rounded-sm border px-2">
      <Search className="mr-2 h-4 w-4 text-muted-foreground" />
      <Input
        ref={inputRef}
        placeholder={placeholder}
        value={state}
        onChange={handleChange}
        className={cn(
          "h-8 w-52 border-none p-0 focus-visible:ring-0 focus-visible:ring-offset-0",
          className,
        )}
      />

      <Button
        variant="ghost"
        size="xs"
        className={cn("h-full p-1 pr-0 hover:bg-transparent", !value && "invisible")}
        onClick={handleClear}
      >
        <X className="h-4 w-4 text-muted-foreground" />
      </Button>
    </div>
  );
};
