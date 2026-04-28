
import { Input } from "@/components/ui-kits/input/input";
import { cn } from "@/lib/utils";
import { X } from "lucide-react";
import { useState } from "react";

import { createContext, useContext } from "react";

interface ChipsContextProps {
  value: string[];
  inputValue: string;
  error: string | null;
  handleInputChange: (value: string) => void;
  handleKeyDown: (e: React.KeyboardEvent<HTMLInputElement>) => void;
  handleRemoveChip: (item: string) => void;
}

export const ChipsContext = createContext<ChipsContextProps | null>(null);

export const useChipsContext = () => {
  const ctx = useContext(ChipsContext);
  if (!ctx) throw new Error("ChipsInput components must be used within <ChipsInput>");
  return ctx;
};

export const ChipsInputList = () => {
  const { value, handleRemoveChip } = useChipsContext();
  return (
    <>
      {value.map((item, index) => (
        <div
          key={`${item}-${index}`}
          className="flex h-fit items-center gap-2 rounded-sm border bg-muted px-2 py-0.5 text-sm"
        >
          <span className="break-all">{item}</span>
          <span
            className="cursor-pointer"
            onClick={() => handleRemoveChip(item)}
            aria-label={`Remove ${item}`}
          >
            <X className="h-3.5 w-3.5" />
          </span>
        </div>
      ))}
    </>
  );
};

export interface ChipsInputFieldProps {
  className?: string;
}

export const ChipsInputField = ({ className }: ChipsInputFieldProps) => {
  const { inputValue, handleInputChange, handleKeyDown, error } = useChipsContext();

  return (
    <>
      <Input
        value={inputValue}
        onChange={(e) => handleInputChange(e.target.value)}
        onKeyDown={handleKeyDown}
        placeholder="Type and press enter"
        className={cn(
          "h-fit min-w-40 flex-1 border-0 p-0 py-0.5 text-sm focus-visible:ring-0 focus-visible:ring-offset-0",
          error && "text-error",
          className,
        )}
      />
      {error && <div className="mt-1 w-full text-xs text-error">{error}</div>}
    </>
  );
};

interface ChipsInputRootProps {
  children: React.ReactNode;
  value: string[];
  onChange: (value: string[]) => void;
  validatorRegex?: RegExp;
  customValidator?: (value: string) => boolean;
  validatorRegexErrorMessage?: string;
  className?: string;
}

export const ChipsInput = ({
  children,
  value = [],
  onChange,
  validatorRegex,
  customValidator,
  validatorRegexErrorMessage = "Invalid format",
  className,
}: ChipsInputRootProps) => {
  const [inputValue, setInputValue] = useState("");
  const [error, setError] = useState<string | null>(null);

  const handleInputChange = (val: string) => {
    setInputValue(val);

    if (val.trim()) {
      if (customValidator) {
        if (!customValidator(val.trim())) {
          setError(validatorRegexErrorMessage);
        } else {
          setError(null);
        }
      } else if (validatorRegex) {
        if (!validatorRegex.test(val.trim())) {
          setError(validatorRegexErrorMessage);
        } else {
          setError(null);
        }
      }
    } else {
      setError(null);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") {
      e.preventDefault();
      if (inputValue.trim() && !error) {
        onChange([...value, inputValue.trim()]);
        setInputValue("");
        setError(null);
      }
    }
  };

  const handleRemoveChip = (item: string) => {
    const updated = value.filter((i) => i !== item);
    onChange(updated);
  };

  return (
    <ChipsContext.Provider
      value={{
        value,
        inputValue,
        error,
        handleInputChange,
        handleKeyDown,
        handleRemoveChip,
      }}
    >
      <div
        className={cn(
          "flex w-full flex-wrap content-start gap-2 rounded-sm border px-3 py-2",
          error && "border-error",
          className,
        )}
      >
        {children}
      </div>
    </ChipsContext.Provider>
  );
};