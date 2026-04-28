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
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui-kits/table/table";
import { useProjectStore } from "@/store/useProjectStore";
import { useGetPermissions } from "@blocks-idp/iam/hooks/use-permission";
import { IPermission, RESOURCE_TYPE } from "@blocks-idp/iam/models/permission";
import { useMemo, useState } from "react";

type AddDependentPermissionProps = {
  permissionsResource: string[];
  onAdd: (data: IPermission[]) => void;
};

export const AddDependentPermission = ({ onAdd, permissionsResource }: AddDependentPermissionProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [open, setOpen] = useState<boolean>(false);
  const [selectedPermisson, setSelectedPermissions] = useState<IPermission[]>([]);
  const [filter, setFilter] = useState({
    page: 0,
    pageSize: 5,
    isBuiltIn: "",
    roles: [],
    type: 1,
    search: "",
  });
  const { data, isLoading } = useGetPermissions({
    ...filter,
    projectKey: tenantId,
  });

  const onClickHandler = async () => {
    onAdd(selectedPermisson);
    resetFilter();
    setOpen(false);
  };

  const onCheckedChangeHandler = (checked: boolean, permission: IPermission) => {
    if (checked) {
      return setSelectedPermissions((prev) => [...prev, permission]);
    }
    setSelectedPermissions((prev) => prev.filter((item) => item.resource !== permission.resource));
  };

  const resetFilter = () => {
    setFilter({
      type: 1,
      page: 0,
      pageSize: 5,
      isBuiltIn: "",
      roles: [],
      search: "",
    });
    setSelectedPermissions([]);
  };

  const selectedPermissionsResource = useMemo(() => {
    return selectedPermisson.map((item) => item.resource) || [];
  }, [selectedPermisson]);

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
          variant="outline"
          onClick={(e) => {
            e.stopPropagation();
          }}
          className="h-[34px]"
        >
          Add
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
                          if (permissionsResource.length + selectedPermisson.length < 5)
                            onCheckedChangeHandler(checked as boolean, item);
                        }}
                      />
                    </TableCell>
                    <TableCell className="w-full">
                      <Badge variant="secondary" className="w-fit break-all">
                        {item.name}
                      </Badge>
                    </TableCell>
                    <TableCell className="w-[100px]">
                      {RESOURCE_TYPE.find((resoruce) => resoruce.value === item.type.toString())?.label}
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
          <Button size="default" onClick={onClickHandler}>
            Add
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
