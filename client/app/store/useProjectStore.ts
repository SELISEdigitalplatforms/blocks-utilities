import { IProject } from "@blocks-identifier/models/project.model";
import { create } from "zustand";
import { persist } from "zustand/middleware";

export interface IProjectStore {
  projects: IProject[];
  selectedProject: IProject | null;
  selectedTenantGroup: string | null;
  setSelectedProject: (project: IProject) => void;
  resetSelectedProject: () => void;
  setProjects: (projects: IProject[]) => void;
  resetProject: () => void;
  reset: () => void;
  setTennantGroup: (tenantGroupId: string) => void;
  resetTennantGroup: () => void;
}

export const useProjectStore = create<IProjectStore>()(
  persist(
    (set) => ({
      projects: [],
      selectedProject: null,
      selectedTenantGroup: null,
      setSelectedProject(project) {
        set((state) => ({ ...state, selectedProject: project }));
        set((state) => ({ ...state, selectedTenantGroup: project.tenantGroupId }));
      },
      resetSelectedProject() {
        set((state) => ({ ...state, selectedProject: null }));
      },
      setProjects(projects) {
        set((state) => ({ ...state, projects: projects }));
      },
      resetProject() {
        set((state) => ({ ...state, projects: [] }));
      },
      reset() {
        set(() => ({ projects: [], selectedProject: null, selectedTenantGroup: null }));
      },
      setTennantGroup(tenantGroupId) {
        set((state) => ({ ...state, selectedTenantGroup: tenantGroupId }));
      },
      resetTennantGroup() {
        set((state) => ({ ...state, selectedTenantGroup: null }));
      },
    }),
    { name: "project-store" },
  ),
);
