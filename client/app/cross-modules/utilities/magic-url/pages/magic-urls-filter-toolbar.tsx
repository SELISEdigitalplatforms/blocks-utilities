import { FilterToolbar, useSortQueryParams } from "@/components/filter-toolbar";
import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";

type MagicUrlFilter = {
  search: string;
  expiryDate: { from?: Date | string; to?: Date | string };
  status: string;
  requestMethod: string;
  type: string;
};

export const useMagicUrlsFilterQueryParams = () => {
  const [queryParams, setQueryParams] = useQueryStates({
    search: parseAsString.withDefault(""),
    expiryStartDate: parseAsString.withDefault(""),
    expiryEndDate: parseAsString.withDefault(""),
    status: parseAsString.withDefault(""),
    requestMethod: parseAsString.withDefault(""),
    type: parseAsString.withDefault(""),
    page: parseAsInteger.withDefault(0),
    pageSize: parseAsInteger.withDefault(10),
  });
  return { queryParams, setQueryParams };
};

export const useMagicUrlSortQueryParams = () =>
  useSortQueryParams({ initial: { property: "OperationName", isDescending: false } });

export function MagicUrlsFilterToolBar() {
  const { queryParams, setQueryParams } = useMagicUrlsFilterQueryParams();

  const updateExpiryDate = (value: { from?: Date; to?: Date } | null) => {
    const { from, to } = value || {};
    const startDate = from || to;
    const endDate = to || from;

    setQueryParams((params) => ({
      ...params,
      expiryStartDate: startDate ? startDate.toISOString() : "",
      expiryEndDate: endDate ? endDate.toISOString() : "",
      page: 0,
    }));
  };

  const changeHandler = (key: string, value: unknown) => {
    if (key === "expiryDate") return updateExpiryDate(value as { from?: Date; to?: Date });
    setQueryParams((params) => ({
      ...params,
      [key]: value,
      page: 0,
    }));
  };

  const resetHandler = () => setQueryParams(null);

  return (
    <FilterToolbar<MagicUrlFilter>
      filters={[
        { key: "search", type: "SearchInput", label: "" },
        {
          key: "status",
          type: "Radio",
          label: "Status",
          props: {
            options: [
              { label: "Active", value: "Active" },
              { label: "Usage Limit Exceeded", value: "UsageLimitExceeded" },
              { label: "Time Expired", value: "TimeExpired" },
            ],
          },
        },
        {
          key: "requestMethod",
          type: "Radio",
          label: "Request Method",
          props: {
            options: [
              { label: "GET", value: "GET" },
              { label: "POST", value: "POST" },
            ],
          },
        },
        {
          key: "expiryDate",
          type: "DateRange",
          label: "Scheduled Expiry Date",
          props: {},
        },
      ]}
      values={{
        search: queryParams.search,
        expiryDate: {
          from: queryParams.expiryStartDate ? new Date(queryParams.expiryStartDate) : "",
          to: queryParams.expiryEndDate ? new Date(queryParams.expiryEndDate) : "",
        },
        status: queryParams.status,
        requestMethod: queryParams.requestMethod,
        type: queryParams.type,
      }}
      defaultValues={{ search: "", expiryDate: { from: "", to: "" }, status: "", requestMethod: "", type: "" }}
      onChange={changeHandler}
      onReset={resetHandler}
    />
  );
}
