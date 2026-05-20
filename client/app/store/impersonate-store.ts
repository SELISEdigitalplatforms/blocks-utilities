import { create } from "zustand";

interface ImpersonateState {
  isImpersonated: boolean;
  impersonatedTenantId: string | null;
  originalTenantId: string | null;

  isInitialized: boolean;

  setImpersonation: (
    isImpersonated: boolean,
    originalTenantId: string | null,
    impersonatedTenantId: string | null,
  ) => void;
  impersonate: (impersonatedTenantId: string, originalTenantId: string) => void;
  terminate: (originalTenantId: string) => void;

  setInitialized: (isInitialized: boolean) => void;

  reset: () => void;
}

export const useImpersonateStore = create<ImpersonateState>()((set) => ({
  isImpersonated: false,
  impersonatedTenantId: null,
  originalTenantId: null,
  isInitialized: false,
  setImpersonation: (
    isImpersonated: boolean,
    originalTenantId: string | null,
    impersonatedTenantId: string | null,
  ) => {
    set({ isImpersonated, impersonatedTenantId, originalTenantId });
  },
  impersonate: (impersonatedTenantId: string, originalTenantId: string) => {
    set({ isImpersonated: true, impersonatedTenantId, originalTenantId });
  },
  terminate: (originalTenantId: string) => {
    set((state) => ({
      ...state,
      isImpersonated: false,
      impersonatedTenantId: null,
      originalTenantId: originalTenantId,
    }));
  },
  setInitialized: (isInitialized: boolean) => {
    set({ isInitialized });
  },
  reset: () => {
    set({
      isImpersonated: false,
      impersonatedTenantId: null,
      originalTenantId: null,
      isInitialized: false,
    });
  },
}));
