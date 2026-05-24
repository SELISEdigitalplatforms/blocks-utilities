import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { RouterProvider } from "react-router-dom";
import { NuqsAdapter } from "nuqs/adapters/react-router/v6";
import { Toaster } from "./components/ui-kits/toaster/toaster";
import QueryProvider from "./providers/query-provider";
import { router } from "./router";
import { ThemeProvider } from "./hooks/use-theme";
import "./styles/globals.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryProvider>
      <ThemeProvider>
        <NuqsAdapter>
          <RouterProvider router={router} />
          <Toaster />
        </NuqsAdapter>
      </ThemeProvider>
    </QueryProvider>
  </StrictMode>,
);
