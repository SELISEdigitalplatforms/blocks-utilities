import { FilterToolbar } from "@/components/filter-toolbar";
import { useAIModelsQueryParams } from "@blocks-ai/hooks/use-aimodel";

type ModelsFilter = {
  search: string;
};

export const AIModelsSelectedPageFilterToolbar = () => {
  const {
    queryParams: { search },
    setQueryParams,
  } = useAIModelsQueryParams();

  const changeHandler = (key: string, value: string) => {
    setQueryParams((params) => ({ ...params, [key]: value }));
  };

  const resetHandler = () => {
    setQueryParams(null);
  };

  return (
    <FilterToolbar<ModelsFilter>
      filters={[{ key: "search", type: "SearchInput", label: "Search" }]}
      values={{ search }}
      defaultValues={{ search: "" }}
      onChange={changeHandler}
      onReset={resetHandler}
    />
  );
};
