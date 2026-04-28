import React from "react";
import { Column } from "@tanstack/react-table";
import { Button } from "@/components/ui-kits/button/button";
import { Calendar } from "@/components/ui-kits/calendar/calendar";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { CalendarIcon } from "lucide-react";
import { DateRange } from "react-day-picker";
import { formatDate } from "@/lib/utils";
import { Separator } from "../ui-kits/separator/separator";
import useIsMobile from "@/hooks/use-is-mobile";

interface DateRangeFilterProps<TData, TValue> {
  column?: Column<TData, TValue>;
  title: string;
  date: DateRange | undefined;
  // eslint-disable-next-line no-unused-vars
  onDateChange: (date: DateRange | undefined) => void;
}

export function DateRangeFilter<TData, TValue>({
  column,
  title,
  date,
  onDateChange,
}: DateRangeFilterProps<TData, TValue>) {
  const isMobile = useIsMobile();

  const handleDateSelect = (selectedDateRange: DateRange | undefined) => {
    onDateChange(selectedDateRange);
    if (selectedDateRange?.from && selectedDateRange?.to) {
      column?.setFilterValue(selectedDateRange);
    } else {
      column?.setFilterValue(undefined);
    }
  };

  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm" className="h-8 border-dashed">
          <div className="flex w-full items-center justify-between">
            <div className="flex items-center">
              <CalendarIcon className="mr-2 h-4 w-4" />
              <span>{title}</span>
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
          selected={date}
          onSelect={handleDateSelect}
          numberOfMonths={isMobile ? 1 : 2}
        />
      </PopoverContent>
    </Popover>
  );
}
