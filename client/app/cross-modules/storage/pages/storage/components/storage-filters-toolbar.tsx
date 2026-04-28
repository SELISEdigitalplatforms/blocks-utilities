import { FilterChangeHandler, FilterToolbar } from "@/components/filter-toolbar";
import { Plus, ChevronDown } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";

type FilterValues = {
  search: string;
  providers: string[];
  types: string[];
};

type StorageFiltersToolbarProps = {
  filters: FilterValues;
  onChange: FilterChangeHandler<FilterValues>;
  onReset: () => void;
  onAddConfiguration: () => void;
};

export function StorageFiltersToolbar({
  filters,
  onChange,
  onReset,
  onAddConfiguration,
}: StorageFiltersToolbarProps) {
  return (
    <div className="mb-6 flex items-center justify-between gap-3">
      <div className="flex-1">
        <FilterToolbar
          filters={[
            { key: "search", type: "SearchInput", label: "Search" },
            {
              key: "providers",
              type: "MultiSelect",
              label: "Provider",
              props: {
                options: [
                  { label: "AWS", value: "Amazon" },
                  { label: "Azure", value: "Azure" },
                  { label: "SFTP", value: "SftpStorage" },
                  { label: "AWS S3 Compatible", value: "S3Compatible" },
                ],
              },
            },
          ]}
          values={filters}
          defaultValues={{ search: "", providers: [], types: [] }}
          onChange={onChange}
          onReset={onReset}
        />
      </div>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="outline" size="sm" className="gap-2">
            Add
            <ChevronDown className="h-4 w-4" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem onClick={onAddConfiguration}>
            <Plus className="mr-2 h-4 w-4" />
            Add Configuration
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  );
}
