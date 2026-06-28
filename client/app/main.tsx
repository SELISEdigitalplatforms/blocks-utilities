import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { RouterProvider } from "react-router-dom";
import { NuqsAdapter } from "nuqs/adapters/react-router/v6";
import { Toaster } from "./components/ui-kits/toaster/toaster";
import QueryProvider from "./providers/query-provider";
import { router } from "./router";
import { ThemeProvider } from "./hooks/use-theme";
import { BlocksAppLayout } from "@seliseblocks/blocks-kit";
import { TooltipProvider } from "./components/ui-kits/tooltip/tooltip";
import "./styles/globals.css";

const darkLogoUrl = "/utilities_logo_black.svg";
const lightLogoUrl = "/utilities_logo_white.svg";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryProvider>
      <NuqsAdapter>
        <ThemeProvider>
          <TooltipProvider>
            <BlocksAppLayout
              config={{
                name: "blocks-utilities",
                appLogoUrl: {
                  dark: lightLogoUrl,
                  light: darkLogoUrl,
                },
              }}
            >
              <RouterProvider router={router} />
            </BlocksAppLayout>
            <Toaster />
          </TooltipProvider>
        </ThemeProvider>
      </NuqsAdapter>
    </QueryProvider>
  </StrictMode>,
);
