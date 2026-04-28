import { useGetRoleById } from "@blocks-idp/iam/hooks/use-roles";
import { IPermission } from "@blocks-idp/iam/models/permission";
import { IRole } from "@blocks-idp/iam/models/role";
import { permissionService } from "@blocks-idp/iam/services/permission.service";
import { useQuery } from "@tanstack/react-query";
import { createContext, useContext, useEffect, useState } from "react";
import { createStore, useStore } from "zustand";

type RoleDetailsStore = ReturnType<typeof createRoleDetailsStore>;

export type PermissionState = IPermission & {
  modified: boolean;
  isInitiallyAssigned: boolean;
  changeState: "added" | "removed" | null;
  parents: string[];
};

export type PermissionGroup = {
  name: string;
  permissions: PermissionState[];
};

export type PermissionMap = Map<string, PermissionState>;
interface RoleDetailsState {
  role: IRole;
  permissions: IPermission[];
  permissionMap: PermissionMap;
  isInitialized: boolean;
  isEditMode: boolean;
  initializeStore: (permissions: IPermission[]) => void;
  changePermissionSelection: (changes: { permissionResource: string; isChecked: boolean }[]) => void;
  changePermissionGroupSelection: (permissions: PermissionState[], isChecked: boolean) => void;
  changeEditMode: (isEditMode: boolean) => void;
  discardChanges: () => void;
}

const createRoleDetailsStore = () => {
  return createStore<RoleDetailsState>()((set, get) => ({
    role: {} as IRole,
    permissions: [],
    groupedPermissions: {},
    permissionMap: new Map<string, PermissionState>(),
    isInitialized: false,
    isEditMode: false,
    initializeStore: (permissions: IPermission[]) => {
      const { role } = get();
      const permissionMap: PermissionMap = new Map();
      const pendingParents = new Map<string, string[]>();

      for (const p of permissions) {
        const isInitiallyAssigned = p.roles.includes(role.slug);
        const parents = pendingParents.get(p.resource) || [];

        permissionMap.set(p.resource, {
          ...p,
          modified: false,
          changeState: null,
          isInitiallyAssigned,
          parents: [...parents],
        });

        // register this permission as a parent for its dependents
        for (const depResource of p.dependentPermissions) {
          if (permissionMap.has(depResource)) {
            const depPerm = permissionMap.get(depResource)!;
            depPerm.parents = [...depPerm.parents, p.resource];
          } else {
            const existingParents = pendingParents.get(depResource) || [];
            pendingParents.set(depResource, [...existingParents, p.resource]);
          }
        }
      }

      set({
        permissions,
        permissionMap,
        isInitialized: true,
        isEditMode: false,
      });
    },

    changePermissionSelection(changes: { permissionResource: string; isChecked: boolean }[]) {
      const { permissionMap } = get();
      for (const { permissionResource, isChecked } of changes) {
        const permission = permissionMap.get(permissionResource);
        if (!permission) continue;
        const modified = isChecked !== permission.isInitiallyAssigned;
        const changeState = isChecked ? (modified ? "added" : null) : modified ? "removed" : null;
        permission.modified = modified;
        permission.changeState = changeState;
      }
      set({ permissionMap: new Map(permissionMap) });
    },

    changePermissionGroupSelection(permissions: PermissionState[], isChecked: boolean) {
      const { changePermissionSelection } = get();
      changePermissionSelection(permissions.map((p) => ({ permissionResource: p.resource, isChecked })));
    },
    changeEditMode: (isEditMode: boolean) => {
      set({ isEditMode });
    },
    discardChanges() {
      const { permissionMap } = get();
      const permissions = Array.from(permissionMap.values());
      const newMap = new Map<string, PermissionState>();
      for (const perm of permissions) {
        newMap.set(perm.resource, {
          ...perm,
          modified: false,
          changeState: null,
        });
      }
      set({ permissionMap: newMap, isEditMode: false });
    },
  }));
};

const RoleDetailsContext = createContext<RoleDetailsStore | null>(null);

export const RoleDetailsProvider = ({
  children,
  id,
  projectKey,
}: {
  children: React.ReactNode;
  id: string;
  projectKey: string;
}) => {
  const [store] = useState(() => createRoleDetailsStore());
  const { data: role } = useGetRoleById({ id, projectKey });
  const { data: permissionsData } = useQuery({
    queryKey: ["permissions"],
    queryFn: () =>
      permissionService.getPermissions({
        page: 0,
        pageSize: 10000,
        roles: [],
        projectKey,
        filter: {
          search: "",
          isBuiltIn: "",
        },
      }),
    refetchOnMount: "always",
  });

  // initilize store when role data changes
  useEffect(() => {
    if (!role?.data) return;
    store.setState((state) => ({ ...state, role: role.data }));
  }, [role?.data, store]);

  // initilize store when permissions data changes
  useEffect(() => {
    if (!permissionsData?.data || !role?.data) return;
    store.getState().initializeStore(permissionsData.data);
  }, [permissionsData?.data, role?.data, store]);

  return <RoleDetailsContext.Provider value={store}>{children}</RoleDetailsContext.Provider>;
};

export const useRoleDetailsStore = <T,>(selector: (state: RoleDetailsState) => T): T => {
  const store = useContext(RoleDetailsContext);
  if (!store) {
    throw new Error("Missing RoleDetailsProvider");
  }
  return useStore(store, selector);
};
