import { FilterToolbar } from "@/components/filter-toolbar";
import { useAIModelsQueryParams } from "@blocks-ai/hooks/use-aimodel";

export function AIModelsFilterToolbar() {
  const { queryParams, setQueryParams } = useAIModelsQueryParams();

  const changeHandler = (key: string, value: unknown) => {
    setQueryParams((prev) => ({
      ...prev,
      [key]: value,
    }));
  };

  const resetHandler = () => setQueryParams(null);

  return (
    <FilterToolbar
      filters={[
        { key: "search", type: "SearchInput", label: "" },
        {
          key: "types",
          type: "MultiSelect",
          label: "Type",
          props: {
            options: [
              { label: "Official API", value: "official" },
              { label: "Open Deployment", value: "open" },
            ],
          },
        },
      ]}
      values={{
        search: queryParams.search,
        types: queryParams.types,
      }}
      defaultValues={{
        search: "",
        types: [],
      }}
      onChange={changeHandler}
      onReset={resetHandler}
    />
  );
}
