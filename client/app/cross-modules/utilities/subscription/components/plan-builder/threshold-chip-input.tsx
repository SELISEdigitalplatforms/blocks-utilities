import { X } from "lucide-react";
import { useState } from "react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Input } from "@/components/ui-kits/input/input";
import { THRESHOLD_PERCENT_PRESETS } from "../../constants/subscription.constants";

/**
 * Removable chips instead of a comma-separated text field — friendlier for an admin who isn't
 * going to remember CSV syntax for something this small.
 */
export const ThresholdChipInput = ({
  value,
  onChange,
}: {
  value: number[];
  onChange: (next: number[]) => void;
}) => {
  const [custom, setCustom] = useState("");

  const add = (percent: number) => {
    if (percent >= 1 && percent <= 100 && !value.includes(percent)) {
      onChange([...value, percent].sort((a, b) => a - b));
    }
  };

  const remove = (percent: number) => onChange(value.filter((existing) => existing !== percent));

  const unusedPresets = THRESHOLD_PERCENT_PRESETS.filter(
    (preset) => !value.includes(preset),
  );

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2">
        {value.map((percent) => (
          <Badge key={percent} variant="secondary" className="gap-1 font-normal">
            {percent}%
            <button
              type="button"
              onClick={() => remove(percent)}
              aria-label={`Remove ${percent}%`}
              className="rounded-full hover:text-destructive"
            >
              <X className="h-3 w-3" />
            </button>
          </Badge>
        ))}

        {unusedPresets.map((preset) => (
          <button
            key={preset}
            type="button"
            onClick={() => add(preset)}
            className="rounded border border-dashed border-muted-foreground/40 px-2 py-1 text-xs text-muted-foreground hover:border-blocks-primary-400 hover:text-blocks-primary-600"
          >
            + {preset}%
          </button>
        ))}
      </div>

      <div className="flex items-center gap-2">
        <Input
          value={custom}
          onChange={(event) => setCustom(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter") {
              event.preventDefault();
              add(Number(custom));
              setCustom("");
            }
          }}
          placeholder="Custom %"
          type="number"
          min={1}
          max={100}
          className="h-8 w-24 text-xs"
        />
        <span className="text-xs text-muted-foreground">Press Enter to add</span>
      </div>
    </div>
  );
};
