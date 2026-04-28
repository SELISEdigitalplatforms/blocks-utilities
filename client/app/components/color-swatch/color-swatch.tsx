import React, { useRef } from "react";
import { cn } from "@/lib/utils";

interface ColorSwatchProps {
  value?: string;
  onChange?: (hex: string) => void;
  className?: string;
  hasError?: boolean;
}

export const ColorSwatch: React.FC<ColorSwatchProps> = ({
  value = "#FFFFFF",
  onChange,
  className = "",
  hasError = false,
}) => {
  const colorInputRef = useRef<HTMLInputElement>(null);

  const handleColorPickerClick = () => {
    colorInputRef.current?.click();
  };

  const handleTextInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    let newValue = e.target.value.toUpperCase();
    newValue = newValue.replace(/[^#0-9A-F]/g, "");
    if (newValue.includes("#")) newValue = "#" + newValue.replace(/#/g, "");
    onChange?.(newValue);
  };

  const handleColorPickerChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const newVal = e.target.value.toUpperCase();
    onChange?.(newVal);
  };

  return (
    <div className={cn("flex w-full flex-col gap-1", className)}>
      <div
        className={cn(
          "flex w-full items-center gap-3 rounded-sm border px-2 py-1.5",
          hasError ? "border-destructive" : "",
        )}
      >
        <div
          className="relative h-6 min-h-6 w-6 min-w-6 cursor-pointer rounded-sm border"
          style={{ backgroundColor: value }}
          onClick={handleColorPickerClick}
          title="Pick a color"
        >
          <input
            ref={colorInputRef}
            type="color"
            value={value}
            onChange={handleColorPickerChange}
            className="absolute inset-0 h-6 w-6 cursor-pointer opacity-0"
          />
        </div>
        <input
          type="text"
          className={cn(
            "box-border h-6 w-24 rounded-sm bg-transparent px-2 text-sm uppercase outline-none",
          )}
          value={value.toUpperCase()}
          onChange={handleTextInputChange}
          maxLength={7}
          placeholder="#FFFFFF"
        />
      </div>
    </div>
  );
};

export const validHexaColorReg = /^#([0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$/;
