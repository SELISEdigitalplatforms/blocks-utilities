import { FilterControls } from "@/components/filter-toolbar";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { cn } from "@/lib/utils";
import { useProjectStore } from "@/store/useProjectStore";
import { useGetPermissions } from "@blocks-idp/iam/hooks/use-permission";
import { IPermission, RESOURCE_TYPE } from "@blocks-idp/iam/models/permission";
import { CirclePlus } from "lucide-react";
import { useMemo, useState } from "react";

type AddSSOPermissionProps = {
  permissions: IPermission[];
  onAdd: (data: IPermission[]) => void;
};

export const AddSSOPermission = ({ onAdd, permissions }: AddSSOPermissionProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [open, setOpen] = useState<boolean>(false);
  const [selectedPermission, setSelectedPermissions] = useState<IPermission[]>([]);
  const [filter, setFilter] = useState({
    page: 0,
    pageSize: 5,
    isBuiltIn: "",
    roles: [],
    search: "",
  });
  const { data, isLoading } = useGetPermissions({
    ...filter,
    projectKey: tenantId,
  });

  const onClickHandler = async () => {
    onAdd(selectedPermission);
    resetFilter();
    setOpen(false);
  };

  const onCheckedChangeHandler = (checked: boolean, permission: IPermission) => {
    if (checked) {
      return setSelectedPermissions((prev) => [...prev, permission]);
    }
    setSelectedPermissions((prev) => prev.filter((item) => item.resource !== permission.resource));
  };

  const handlePermissionCheckboxChange = (checked: boolean, permission: IPermission) => {
    if (checked && permissionsResource.length + selectedPermission.length >= 5) {
      return;
    }
    onCheckedChangeHandler(checked, permission);
  };

  const resetFilter = () => {
    setFilter({
      page: 0,
      pageSize: 5,
      isBuiltIn: "",
      roles: [],
      search: "",
    });
    setSelectedPermissions([]);
  };

  const permissionsResource = useMemo(() => {
    return permissions.map((item) => item.resource) || [];
  }, [permissions]);

  const selectedPermissionsResource = useMemo(() => {
    return selectedPermission.map((item) => item.resource) || [];
  }, [selectedPermission]);

  return (
    <Dialog
      open={open}
      onOpenChange={(v) => {
        if (!v) resetFilter();
        setOpen(v);
      }}
    >
      <DialogTrigger asChild>
        <Button
          size="sm"
          variant="default"
          className="h-10 bg-primary text-sm"
          onClick={(e) => {
            e.stopPropagation();
          }}
          disabled={permissions.length >= 5}
        >
          <CirclePlus className="h-5 w-5 md:mr-2.5" />
          <span className="sr-only sm:not-sr-only">Assign Permissions</span>
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle className="text-left">Assign Permissions</DialogTitle>
          <DialogDescription></DialogDescription>
        </DialogHeader>
        <div>
          <FilterControls.SearchInput
            placeholder="Search by permission name"
            onChange={(search) => setFilter((prev) => ({ ...prev, search, page: 0 }))}
            value={filter.search}
            className="h-fit w-full py-3"
          />
        </div>
        <h1
          className={cn("text-sm font-semibold", selectedPermission.length === 5 && "text-error")}
        >
          *You can select up to 5 permissions. <span>{`(${selectedPermission.length}/5)`}</span>
        </h1>
        <Card>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead></TableHead>
                  <TableHead>Name</TableHead>
                  <TableHead>Type</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data?.data.map((item) => (
                  <TableRow key={item.itemId}>
                    <TableCell>
                      <Checkbox
                        checked={
                          permissionsResource.includes(item.resource) ||
                          selectedPermissionsResource.includes(item.resource)
                        }
                        disabled={permissionsResource.includes(item.resource)}
                        onCheckedChange={(checked) => {
                          handlePermissionCheckboxChange(checked as boolean, item);
                        }}
                      />
                    </TableCell>
                    <TableCell>
                      <Badge variant="secondary" className="w-fit">
                        {item.name}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      {
                        RESOURCE_TYPE.find((resource) => resource.value === item.type.toString())
                          ?.label
                      }
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>

        <div className="flex items-center justify-end">
          {!isLoading && data && data.totalCount > filter.pageSize && (
            <Pagination
              page={filter.page}
              pageSize={filter.pageSize}
              onChange={(page) => setFilter((prev) => ({ ...prev, page }))}
              totalCount={data.totalCount}
            />
          )}
        </div>
        <DialogFooter>
          <DialogClose asChild>
            <Button variant="outline" size="default">
              Cancel
            </Button>
          </DialogClose>
          <Button
            size="default"
            onClick={onClickHandler}
            disabled={selectedPermission.length === 0}
          >
            Add
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
