import { User } from "@blocks-idp/iam/models/user";
import { create } from "zustand";
import { persist } from "zustand/middleware";

interface AuthState {
  isAuthenticated: boolean;
  user: User | null;
  accessToken: string | null;
  refreshToken: string | null;
  setUser: (user: User | null) => void;
  setAuthenticated: () => void;
  setUnAuthenticated: () => void;
  setTokens: (accessToken: string, refreshToken: string) => void;
  clearTokens: () => void;
  reset: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      isAuthenticated: false,
      user: null,
      accessToken: null,
      refreshToken: null,
      setUser: (user: User | null) => {
        set((state) => ({ ...state, user }));
      },
      setAuthenticated: () => {
        set((state) => ({ ...state, isAuthenticated: true }));
      },
      setUnAuthenticated: () => {
        set((state) => ({ ...state, isAuthenticated: false, user: null }));
      },
      setTokens: (accessToken: string, refreshToken: string) => {
        set((state) => ({ ...state, accessToken, refreshToken }));
      },
      clearTokens: () => {
        set((state) => ({ ...state, accessToken: null, refreshToken: null }));
      },
      reset: () => {
        set({
          isAuthenticated: false,
          user: null,
          accessToken: null,
          refreshToken: null,
        });
      },
    }),
    {
      name: "auth-storage",
    },
  ),
);
