

import { FilterToolbar, useSortQueryParams } from "@/components/filter-toolbar";
import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";

export const useOrganizationsFilterQueryParams = () => {
  const [queryParams, setQueryParams] = useQueryStates({
    search: parseAsString.withDefault(""),
    page: parseAsInteger.withDefault(0),
    pageSize: parseAsInteger.withDefault(10),
  });
  return { queryParams, setQueryParams };
};

export const useOrganizationsSortQueryParams = () =>
  useSortQueryParams({ initial: { property: "Name", isDescending: false } });

export function OrganizationsFilterToolbar() {
  const { queryParams, setQueryParams } = useOrganizationsFilterQueryParams();

  const changeHandler = (key: string, value: unknown) => {
    setQueryParams((prev) => ({
      ...prev,
      [key]: value,
      page: 0,
    }));
  };

  const resetHandler = () => setQueryParams(null);

  return (
    <FilterToolbar
      filters={[{ key: "search", type: "SearchInput", label: "label" }]}
      values={{
        search: queryParams.search,
      }}
      defaultValues={{ search: "" }}
      onChange={changeHandler}
      onReset={resetHandler}
    />
  );
}
