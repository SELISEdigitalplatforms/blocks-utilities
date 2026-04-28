import React, { useRef, useEffect } from "react";
import { Search, X } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";

interface SearchInputProps {
  // eslint-disable-next-line no-unused-vars
  onSearch: (value: string) => void;
  placeholder?: string;
  value: string;
  className?: string;
  toggleable?: boolean;
  isVisible: boolean;
  // eslint-disable-next-line no-unused-vars
  setIsVisible: (isVisible: boolean) => void;
}

export const SearchInput: React.FC<SearchInputProps> = ({
  onSearch,
  placeholder = "Search...",
  value,
  className = "",
  toggleable = false,
  isVisible,
  setIsVisible,
}) => {
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (isVisible && inputRef.current) {
      inputRef.current.focus();
    }
  }, [isVisible]);

  const toggleSearch = () => setIsVisible(!isVisible);

  const handleChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    onSearch(event.target.value);
  };

  const handleClear = () => {
    onSearch("");
    if (toggleable) {
      setIsVisible(false);
    } else if (inputRef.current) {
      inputRef.current.focus();
    }
  };

  if (toggleable && !isVisible) {
    return (
      <Button variant="ghost" size="sm" className="p-0 px-2" onClick={toggleSearch}>
        <Search size={20} />
      </Button>
    );
  }

  return (
    <div className="relative">
      <Input
        ref={inputRef}
        placeholder={placeholder}
        value={value}
        onChange={handleChange}
        className={`pr-8 ${className}`}
      />
      {value && (
        <Button
          variant="ghost"
          size="sm"
          className="absolute right-0 top-0 h-full px-3 py-2 hover:bg-transparent"
          onClick={handleClear}
        >
          <X className="h-4 w-4 text-muted-foreground" />
        </Button>
      )}
    </div>
  );
};
