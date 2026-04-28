import { FilterControls } from "@/components/filter-toolbar";
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

import { useProjectStore } from "@/store/useProjectStore";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { IRole } from "@blocks-idp/iam/models/role";
import { CirclePlus } from "lucide-react";
import { useMemo, useState } from "react";

type AddSSORoleProps = {
  roles: IRole[];
  onAdd: (data: IRole[]) => void;
};

export const AddSSORole = ({ onAdd, roles }: AddSSORoleProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [open, setOpen] = useState<boolean>(false);
  const [selectedRolos, setSelectedRoles] = useState<IRole[]>([]);
  const [filter, setFilter] = useState({ page: 0, pageSize: 10, search: "" });

  const { data, isLoading } = useGetRoles({
    page: filter.page,
    pageSize: filter.pageSize,
    projectKey: tenantId,
    sort: { property: "Name", isDescending: false },
    filter: {
      search: filter.search,
    },
  });

  const onCheckedChangeHandler = (checked: boolean, role: IRole) => {
    if (checked) {
      return setSelectedRoles((roles) => [...roles, role]);
    }
    setSelectedRoles((roles) => roles.filter((item) => item.slug !== role.slug));
  };

  const pageChangeHandler = (page: number) => setFilter((prev) => ({ ...prev, page }));

  const reset = () => {
    setSelectedRoles([]);
    setFilter({ page: 0, pageSize: 10, search: "" });
  };

  const rolesSlug = useMemo(() => {
    return roles.map((item) => item.slug) || [];
  }, [roles]);

  const selectedRolesSlug = useMemo(() => {
    return selectedRolos.map((item) => item.slug) || [];
  }, [selectedRolos]);

  return (
    <Dialog
      open={open}
      onOpenChange={(value) => {
        if (!value) reset();
        setOpen(value);
      }}
    >
      <DialogTrigger asChild>
        <Button size="sm" variant="default" className="h-10 bg-primary text-sm" type="button">
          <CirclePlus className="h-5 w-5 md:mr-2.5" />
          <span className="sr-only sm:not-sr-only">Assign Role</span>
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle className="text-left">Assign roles</DialogTitle>
          <DialogDescription></DialogDescription>
        </DialogHeader>
        <div>
          <FilterControls.SearchInput
            value={filter.search}
            onChange={(value) => setFilter((prev) => ({ ...prev, search: value, page: 0 }))}
            className="h-fit w-full py-3"
            placeholder="Search by role name"
          />
        </div>
        <Card>
          <CardContent>
            <div className="grid grid-cols-2">
              {isLoading ? (
                // Show skeletons while loading
                Array.from({ length: filter.pageSize }).map((_, idx) => (
                  <div key={idx} className="flex animate-pulse items-center space-x-2 py-2">
                    <div className="h-4 w-4 rounded bg-gray-200" />
                    <div className="h-4 w-24 rounded bg-gray-200" />
                    <div className="h-4 w-20 rounded bg-gray-200" />
                  </div>
                ))
              ) : data && data.data && data.data.length > 0 ? (
                data.data.map((item) => (
                  <div key={item.itemId} className="col-span-1 flex items-center py-2">
                    <Checkbox
                      checked={
                        rolesSlug.includes(item.slug) || selectedRolesSlug.includes(item.slug)
                      }
                      disabled={rolesSlug.includes(item.slug)}
                      onCheckedChange={(value) => onCheckedChangeHandler(!!value, item)}
                    />
                    <div className="ml-2 flex flex-col">
                      <div className="max-w-[150px] truncate" title={item.name}>
                        {item.name}
                      </div>
                      <div
                        className="max-w-[150px] truncate text-sm text-muted-foreground"
                        title={item.slug}
                      >
                        {item.slug}
                      </div>
                    </div>
                  </div>
                ))
              ) : (
                <div className="flex h-24 items-center justify-center">No roles are found</div>
              )}
            </div>
            {/* <Table>
              <TableHeader>
                <TableRow>
                  <TableHead></TableHead>
                  <TableHead>Name</TableHead>
                  <TableHead>Slug</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data && data.data && data.data.length > 0 ? (
                  data?.data.map((item) => (
                    <TableRow key={item.itemId}>
                      <TableCell>
                        <Checkbox
                          checked={
                            rolesSlug.includes(item.slug) || selectedRolesSlug.includes(item.slug)
                          }
                          disabled={rolesSlug.includes(item.slug)}
                          onCheckedChange={(value) => onCheckedChangeHandler(!!value, item)}
                        />
                      </TableCell>
                      <TableCell>
                        <div className="max-w-[150px] truncate" title={item.name}>
                          {item.name}
                        </div>
                      </TableCell>
                      <TableCell>
                        <Badge className="w-fit" variant="secondary">
                          <div className="max-w-[150px] truncate" title={item.slug}>
                            {item.slug}
                          </div>
                        </Badge>
                      </TableCell>
                    </TableRow>
                  ))
                ) : (
                  <TableRow className="h-24">
                    <TableCell colSpan={3} className="cell-s text-center">
                      No roles are found
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table> */}
          </CardContent>
        </Card>
        <div>
          {!isLoading && data && data.totalCount > filter.pageSize && (
            <div className="flex items-center md:justify-end">
              <Pagination
                page={filter.page}
                onChange={pageChangeHandler}
                totalCount={data.totalCount || 0}
                pageSize={filter.pageSize}
              />
            </div>
          )}
        </div>
        <DialogFooter>
          <DialogClose asChild>
            <Button variant="outline" size="default">
              Cancel
            </Button>
          </DialogClose>
          <Button
            type="button"
            size="default"
            onClick={() => {
              onAdd(selectedRolos);
              reset();
              setOpen(false);
            }}
          >
            Add
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
