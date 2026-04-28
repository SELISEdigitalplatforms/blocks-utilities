import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
  Table,
} from "@/components/ui-kits/table/table";
import { RESOURCE_TYPE, ResourceType } from "@blocks-idp/iam/models/permission";
import { Checkbox } from "@radix-ui/react-checkbox";
import {
  Select,
  SelectTrigger,
  SelectValue,
  SelectContent,
  SelectItem,
} from "@radix-ui/react-select";
import { TabsContent } from "@radix-ui/react-tabs";
import { ChevronLeft, ChevronRight } from "lucide-react";

type MembershipPermissionsTabProps = {
  isEditing: boolean;
  permissionsSearch: string;
  onPermissionsSearchChange: (value: string) => void;
  permissionsTypeFilter: string;
  onPermissionsTypeFilterChange: (value: string) => void;
  permissionsPage: number;
  onPermissionsPageChange: (page: number) => void;
  isPermissionsLoading: boolean;
  allPermissions: { itemId: string; name: string; type: number; roles?: string[] }[];
  selectedPermissions: string[];
  totalPermissions: number;
  totalPermissionPages: number;
  onPermissionToggle: (permissionName: string) => void;
};

export const MembershipPermissionsTab = ({
  isEditing,
  permissionsSearch,
  onPermissionsSearchChange,
  permissionsTypeFilter,
  onPermissionsTypeFilterChange,
  permissionsPage,
  onPermissionsPageChange,
  isPermissionsLoading,
  allPermissions,
  selectedPermissions,
  totalPermissions,
  totalPermissionPages,
  onPermissionToggle,
}: MembershipPermissionsTabProps) => (
  <TabsContent value="permissions" className="mt-4">
    {isEditing ? (
      <div className="flex flex-col gap-4">
        <p className="text-sm font-medium">Assigned Permissions</p>
        <div className="flex gap-2">
          <Input
            data-testid="permission-tab-searchbar"
            name="permission-tab-searchbar"
            placeholder="Search"
            value={permissionsSearch}
            onChange={(e) => onPermissionsSearchChange(e.target.value)}
            className="flex-1 focus-visible:ring-inset focus-visible:ring-offset-0"
          />
          <Select
            value={permissionsTypeFilter}
            onValueChange={(value) => onPermissionsTypeFilterChange(value)}
          >
            <SelectTrigger className="w-[120px]">
              <SelectValue placeholder="Type" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All</SelectItem>
              {RESOURCE_TYPE.map((type) => (
                <SelectItem key={type.value} value={type.value}>
                  {type.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        {isPermissionsLoading ? (
          <div className="space-y-2">
            {Array.from({ length: 5 }).map((_, i) => (
              <Skeleton key={i} className="h-10 w-full" />
            ))}
          </div>
        ) : (
          <>
            <div className="rounded-md border">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="w-[50px]"></TableHead>
                    <TableHead>Permission</TableHead>
                    <TableHead>Type</TableHead>
                    <TableHead>Role</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {allPermissions.map((permission) => (
                    <TableRow key={permission.itemId}>
                      <TableCell>
                        <Checkbox
                          checked={selectedPermissions.includes(permission.name)}
                          onCheckedChange={() => onPermissionToggle(permission.name)}
                        />
                      </TableCell>
                      <TableCell className="max-w-[150px] truncate">{permission.name}</TableCell>
                      <TableCell>{ResourceType[permission.type] || permission.type}</TableCell>
                      <TableCell>{permission.roles?.join(", ") || "-"}</TableCell>
                    </TableRow>
                  ))}
                  {allPermissions.length === 0 && (
                    <TableRow>
                      <TableCell colSpan={4} className="text-center text-muted-foreground">
                        No permissions found
                      </TableCell>
                    </TableRow>
                  )}
                </TableBody>
              </Table>
            </div>
            <div className="flex items-center justify-between text-sm text-muted-foreground">
              <span>
                Showing {permissionsPage * 10 + 1}-
                {Math.min((permissionsPage + 1) * 10, totalPermissions)} of {totalPermissions}{" "}
                permissions
              </span>
              <div className="flex items-center gap-2">
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => onPermissionsPageChange(Math.max(0, permissionsPage - 1))}
                  disabled={permissionsPage === 0}
                >
                  <ChevronLeft className="h-4 w-4" />
                  Previous
                </Button>
                <div className="flex items-center gap-1">
                  {Array.from({ length: Math.min(3, totalPermissionPages) }).map((_, i) => {
                    const pageNum = i;
                    return (
                      <Button
                        key={pageNum}
                        variant={permissionsPage === pageNum ? "default" : "ghost"}
                        size="sm"
                        className="h-8 w-8 p-0"
                        onClick={() => onPermissionsPageChange(pageNum)}
                      >
                        {pageNum + 1}
                      </Button>
                    );
                  })}
                  {totalPermissionPages > 3 && <span>...</span>}
                </div>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() =>
                    onPermissionsPageChange(Math.min(totalPermissionPages - 1, permissionsPage + 1))
                  }
                  disabled={permissionsPage >= totalPermissionPages - 1}
                >
                  Next
                  <ChevronRight className="h-4 w-4" />
                </Button>
              </div>
            </div>
          </>
        )}
      </div>
    ) : (
      <div className="flex flex-col gap-4">
        <p className="text-sm font-medium">Assigned Permissions</p>
        <div className="flex gap-2">
          <Input
            placeholder="Search"
            value={permissionsSearch}
            onChange={(e) => onPermissionsSearchChange(e.target.value)}
            className="flex-1 focus-visible:ring-inset focus-visible:ring-offset-0"
          />
          <Select value={permissionsTypeFilter} onValueChange={onPermissionsTypeFilterChange}>
            <SelectTrigger className="w-[120px]">
              <SelectValue placeholder="Type" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All</SelectItem>
              {RESOURCE_TYPE.map((type) => (
                <SelectItem key={type.value} value={type.value}>
                  {type.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="rounded-md border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Permission</TableHead>
                <TableHead>Type</TableHead>
                <TableHead>Role</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {selectedPermissions
                .filter((p) => p.toLowerCase().includes(permissionsSearch.toLowerCase()))
                .map((permission, index) => {
                  const permissionData = allPermissions.find((p) => p.name === permission);
                  return (
                    <TableRow key={index}>
                      <TableCell className="max-w-[150px] truncate">{permission}</TableCell>
                      <TableCell>
                        {permissionData
                          ? ResourceType[permissionData.type] || permissionData.type
                          : "-"}
                      </TableCell>
                      <TableCell>{permissionData?.roles?.join(", ") || "-"}</TableCell>
                    </TableRow>
                  );
                })}
              {selectedPermissions.length === 0 && (
                <TableRow>
                  <TableCell colSpan={3} className="text-center text-muted-foreground">
                    No permissions assigned
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </div>
        <span className="text-sm text-muted-foreground">
          Showing 1-{selectedPermissions.length} of {selectedPermissions.length} permissions
        </span>
      </div>
    )}
  </TabsContent>
);
