

import { FilterToolbar, useSortQueryParams } from "@/components/filter-toolbar";
import { PERMISSION_SEVERITY_OPTIONS, RESOURCE_TYPE } from "@blocks-idp/iam/models/permission";
import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";

export const usePermissionsFilterQuaryParams = () => {
  const [queryParams, setQueryParams] = useQueryStates({
    search: parseAsString.withDefault(""),
    isBuiltIn: parseAsString.withDefault(""),
    type: parseAsString.withDefault(""),
    page: parseAsInteger.withDefault(0),
    pageSize: parseAsInteger.withDefault(10),
    permissionSeverity: parseAsString.withDefault(""),
  });
  return { queryParams, setQueryParams };
};
export const usePermissionsSortQuaryParams = () =>
  useSortQueryParams({ initial: { property: "Name", isDescending: false } });

export function PermissionsFilterToolbar() {
  const { queryParams, setQueryParams } = usePermissionsFilterQuaryParams();

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
      filters={[
        { key: "search", type: "SearchInput", label: "label" },
        {
          key: "isBuiltIn",
          type: "Radio",
          label: "Source",
          props: {
            options: [
              { label: "Built-in", value: "yes" },
              { label: "Custom", value: "no" },
            ],
          },
        },
        {
          key: "permissionSeverity",
          type: "Radio",
          label: "Severity",
          props: {
            options: PERMISSION_SEVERITY_OPTIONS.map((option) => ({
              label: option.label,
              value: option.value.toString(),
            })),
          },
        },
        {
          key: "type",
          type: "Radio",
          label: "Type",
          props: { options: RESOURCE_TYPE },
        },
      ]}
      values={{
        search: queryParams.search,
        type: queryParams.type,
        isBuiltIn: queryParams.isBuiltIn,
        permissionSeverity: queryParams.permissionSeverity,
      }}
      defaultValues={{ search: "", isBuiltIn: "", type: "", permissionSeverity: "" }}
      onChange={changeHandler}
      onReset={resetHandler}
    />
  );
}
