import { FilterToolbar } from "@/components/filter-toolbar";
import { useContext } from "react";
import { LogsViewerContext } from "../logs-viewer";
import { LOG_LEVEL } from "../../utils";

export const LogsFilterToolbar = () => {
  const { filter, setFilter, resetFilter } = useContext(LogsViewerContext);
  const { level, startDate, endDate, search } = filter || {
    level: "",
    startDate: "",
    endDate: "",
    search: "",
  };

  const levels = Object.entries(LOG_LEVEL).map((item) => ({
    label: item[0],
    value: item[1],
  }));

  const updateFilter = (key: keyof typeof filter, value: unknown) => {
    setFilter((filter) => ({
      ...filter,
      [key]: value,
    }));
  };

  const updateDate = (value: { from?: Date; to?: Date } | null) => {
    const { from, to } = value || {};
    setFilter((filter) => ({
      ...filter,
      startDate: from ? from.toISOString() : "",
      endDate: to ? to.toISOString() : "",
    }));
  };

  const onChange = (key: string, value: unknown) => {
    if (key === "date") return updateDate(value as { from?: Date; to?: Date });
    return updateFilter(key as keyof typeof filter, value);
  };

  return (
    <FilterToolbar
      filters={[
        { key: "search", type: "SearchInput", label: "label" },
        {
          key: "date",
          type: "DateRange",
          label: "Date",
          props: {},
        },
        {
          key: "level",
          type: "Radio",
          label: "Type",
          props: { options: levels },
        },
      ]}
      values={{
        search,
        level,
        date: {
          from: startDate ? new Date(startDate) : "",
          to: endDate ? new Date(endDate) : "",
        },
      }}
      defaultValues={{ search: "", level: "", date: { from: "", to: "" } }}
      onChange={onChange}
      onReset={resetFilter}
    />
  );
};
