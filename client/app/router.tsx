import { createBrowserRouter, Navigate, Outlet } from "react-router-dom";
import MagicUrlPage from "./routes/dashboard/magic-url";
import MagicUrlDetailsPage from "./routes/dashboard/magic-url-details";
import PaymentPage from "./routes/dashboard/payment";
import PaymentCreatePage from "./routes/dashboard/payment-create";
import PaymentMethodsPage from "./routes/dashboard/payment-methods";
import PaymentResultRoute from "./routes/dashboard/payment-result";
import PaymentProvidersPage from "./routes/dashboard/payment-providers";
import PaymentProviderCreatePage from "./routes/dashboard/payment-provider-create";
import PaymentProviderUpdatePage from "./routes/dashboard/payment-provider-update";
import PaymentProviderRotatePage from "./routes/dashboard/payment-provider-rotate";
import {
  AuthResolver,
  PublicGuard,
  LoginPage,
  ProtectedGuard,
  ConsoleLayout,
  ConsolePage,
  CallbackPage,
  ProfilePage,
  DashboardOverview,
} from "@seliseblocks/blocks-kit";
import {
  DashboardRoute
} from "@seliseblocks/blocks-kit/layouts";
import { ErrorBoundary } from "@/components/error-boundary";
import { navigationMenus } from "./constants/navigation-menus";

const redirectPaths: Record<string, string> = {
  "/services/language/translations/*": "/services/language",
};

export const router = createBrowserRouter([
  {
    element: (
      <ErrorBoundary>
        <Outlet />
      </ErrorBoundary>
    ),
    children: [
      {
        path: "/login/callback",
        element: <CallbackPage defaultRedirectUrl="/app/console" />,
      },
      {
        element: (
          <AuthResolver>
            <Outlet />
          </AuthResolver>
        ),
        children: [
          {
            element: (
              <PublicGuard>
                <Outlet />
              </PublicGuard>
            ),
            children: [{ path: "/login", element: <LoginPage /> }],
          },
          {
            path: "/app",
            element: (
              <ProtectedGuard>
                <Outlet />
              </ProtectedGuard>
            ),
            children: [
              {
                index: true,
                element: <Navigate to="console" replace />,
              },
              {
                element: (
                  <ConsoleLayout>
                    <Outlet />
                  </ConsoleLayout>
                ),
                children: [
                  { path: "profile", element: <ProfilePage /> },
                  { path: "console", element: <ConsolePage /> },
                ],
              },
              {
                path: ":itemId",
                element: (
                  <DashboardRoute
                    redirectPaths={redirectPaths}
                    navigationMenus={navigationMenus}
                  />
                ),
                children: [
                  {
                    index: true,
                    element: <Navigate to="dashboard" replace />,
                  },
                  { path: "dashboard", element: <DashboardOverview /> },
                  { path: "payment", element: <PaymentPage /> },
                  { path: "payment/list", element: <PaymentPage /> },
                  {
                    path: "payment/create",
                    element: <PaymentCreatePage />,
                  },
                  {
                    path: "payment/cards",
                    element: <PaymentMethodsPage />,
                  },
                  {
                    path: "payment/providers",
                    element: <PaymentProvidersPage />,
                  },
                  {
                    path: "payment/providers/create",
                    element: <PaymentProviderCreatePage />,
                  },
                  {
                    path: "payment/providers/:paymentProviderId/edit",
                    element: <PaymentProviderUpdatePage />,
                  },
                  {
                    path: "payment/providers/:paymentProviderId/rotate",
                    element: <PaymentProviderRotatePage />,
                  },
                  {
                    path: "payment/result",
                    element: <PaymentResultRoute />,
                  },
                  { path: "magic-url", element: <MagicUrlPage /> },
                  {
                    path: "magic-url/details/:id",
                    element: <MagicUrlDetailsPage />,
                  },
                ],
              },
            ],
          },
          { path: "/", element: <Navigate to="/app/console" replace /> },
          { path: "*", element: <Navigate to="/login" replace /> },
        ],
      },
    ],
  },
]);
