
import { FilterToolbar, useSortQueryParams } from "@/components/filter-toolbar";
import { Mail, User } from "lucide-react";
import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";

type UsersFilter = {
  search: {};
};

export const useUsersFilterQueryParams = () => {
  const [queryParams, setQueryParams] = useQueryStates({
    page: parseAsInteger.withDefault(0),
    pageSize: parseAsInteger.withDefault(10),
    "selected-filter": parseAsString.withDefault("name"),
    name: parseAsString.withDefault(""),
    email: parseAsString.withDefault(""),
  });
  return { queryParams, setQueryParams };
};

export const useUsersSortQueryParams = () =>
  useSortQueryParams({
    initial: { property: "FirstName", isDescending: false },
  });

export const UsersFilterToolbar = () => {
  const { queryParams, setQueryParams } = useUsersFilterQueryParams();

  const changeHandler = (key: string, value: unknown) => {
    if (key === "search") {
      const val = value as { selected: "name" | "email"; value: string };
      return setQueryParams((params) => ({
        ...params,
        "selected-filter": val.selected,
        name: val.selected === "name" ? val.value : "",
        email: val.selected === "email" ? val.value : "",
        page: 0,
      }));
    }

    setQueryParams((params) => ({
      ...params,
      [key]: value,
      page: 0,
    }));
  };
  const resetHandler = () => {
    setQueryParams(null);
  };

  return (
    <FilterToolbar<UsersFilter>
      filters={[
        {
          key: "search",
          type: "DropdownSearchInput",
          label: "",
          props: {
            className: {
              selectContent: "min-w-fit",
              SelectItem: "[&>*:first-child]:hidden flex justify-center px-2",
            },
            options: [
              { label: <Mail className="aspect-square w-4" />, value: "email" },
              { label: <User className="aspect-square w-4" />, value: "name" },
            ],
          },
        },
      ]}
      values={{
        search: {
          selected: queryParams["selected-filter"],
          value: queryParams["selected-filter"] === "email" ? queryParams.email : queryParams.name,
        },
      }}
      defaultValues={{ search: { selected: "name", value: "" } }}
      onChange={changeHandler}
      onReset={resetHandler}
      hideGlobalResetButton
    />
  );
};
