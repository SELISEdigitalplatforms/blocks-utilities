import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";

/**
 * Creates a React Query provider wrapper for renderHook/render in tests.
 * Retries are disabled and gcTime is 0 so async states resolve deterministically.
 */
export const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0 },
      mutations: { retry: false },
    },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};
