

import { FilterChangeHandler, FilterToolbar, useSortQueryParams } from "@/components/filter-toolbar";
import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";

export const useTemplatesFilterQueryParams = () => {
  const [queryParams, setQueryParams] = useQueryStates({
    pageNumber: parseAsInteger.withDefault(0),
    pageSize: parseAsInteger.withDefault(10),
    search: parseAsString.withDefault(""),
    language: parseAsString.withDefault(""),
    mailConfigurationId: parseAsString.withDefault(""),
  });
  return { queryParams, setQueryParams };
};
export const useTemplatesSortQueryParams = () =>
  useSortQueryParams({ initial: { property: "Name", isDescending: false } });

export function TemplateFilterToolbar({
  emailConfigsData,
  languageListData,
}: {
  emailConfigsData: Array<{ itemId: string; name: string }>;
  languageListData: Array<{ itemId: string; languageName: string; languageCode: string; isDefault?: boolean }>;
}) {
  const { queryParams, setQueryParams } = useTemplatesFilterQueryParams();
  const onChange: FilterChangeHandler<{ search: string; language: string; mailConfigurationId: string }> = (
    _key,
    _value,
    values
  ) => {
    setQueryParams((prev) => ({
      ...prev,
      ...values,
      pageNumber: 0,
    }));
  };
  const onReset = () => {
    setQueryParams(null);
  };
  return (
    <FilterToolbar
      filters={[
        { key: "search", type: "SearchInput", label: "label" },
        {
          key: "mailConfigurationId",
          type: "Radio",
          label: "Mail Configuration",
          props: {
            options: emailConfigsData.map((config) => ({
              label: config.name,
              value: config.itemId,
            })),
          },
        },
        {
          key: "language",
          type: "Radio",
          label: "Language",
          props: {
            options: languageListData.map((lang) => ({
              label: lang.languageName,
              value: lang.languageCode,
            })),
          },
        },
      ]}
      values={{
        search: queryParams.search,
        language: queryParams.language,
        mailConfigurationId: queryParams.mailConfigurationId,
      }}
      defaultValues={{ search: "", language: "", mailConfigurationId: "" }}
      onChange={onChange}
      onReset={onReset}
    />
  );
}
