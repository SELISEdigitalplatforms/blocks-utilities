import { Button } from "@/components/ui-kits/button/button";
import { Calendar } from "@/components/ui-kits/calendar/calendar";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { CalendarIcon } from "lucide-react";
import { formatDate } from "@/lib/utils";
import useIsMobile from "@/hooks/use-is-mobile";
import { Separator } from "@/components/ui-kits/separator/separator";
import { MouseEvent, useEffect, useState } from "react";

type DateRangeType = { from?: Date; to?: Date } | null;

interface DateRangeFilterProps {
  label: string;
  value: DateRangeType;
  onChange: (date: DateRangeType) => void;
}

export function DateRange({ label, value, onChange }: DateRangeFilterProps) {
  const isMobile = useIsMobile();
  const [open, setOpen] = useState<boolean>(false);
  const [date, setDate] = useState<DateRangeType>(value);

  useEffect(() => {
    if (!open) {
      setDate(value);
    }
  }, [open, value]);

  const handleDateSelect = (selectedDateRange: DateRangeType | undefined) => {
    if (!selectedDateRange) return setDate(null);
    setDate(selectedDateRange);
  };

  const resetBtnHandler = (event: MouseEvent) => {
    event.stopPropagation();
    setDate(null);
  };
  const applyBtnHandler = (event: MouseEvent) => {
    event.stopPropagation();
    onChange(date);
    setOpen(false);
  };

  return (
    <Popover
      open={open}
      onOpenChange={(open) => {
        setOpen(open);
        setDate(value);
      }}
    >
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm" className="h-8 border-dashed">
          <div className="flex w-full items-center justify-between">
            <div className="flex items-center">
              <CalendarIcon className="mr-2 h-4 w-4" />
              <span>{label}</span>
            </div>
            {date?.from && (
              <>
                <Separator orientation="vertical" className="hidden h-4 sm:mx-2 sm:block" />

                {formatDate(date.from, true)}
                {date.to && (
                  <>
                    {" - "}
                    {formatDate(date.to, true)}
                  </>
                )}
              </>
            )}
          </div>
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-auto p-0" align="start">
        <Calendar
          initialFocus
          mode="range"
          defaultMonth={date?.from}
          selected={date?.from ? { from: date.from, to: date.to } : undefined}
          onSelect={handleDateSelect}
          numberOfMonths={isMobile ? 1 : 2}
        />
        <div className="flex items-center gap-4 px-3 pb-4">
          <Button type="button" variant="outline" className="w-full" onClick={resetBtnHandler}>
            Reset
          </Button>
          <Button type="button" className="w-full" onClick={applyBtnHandler}>
            Apply
          </Button>
        </div>
      </PopoverContent>
    </Popover>
  );
}
