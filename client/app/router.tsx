import { createBrowserRouter, Navigate, Outlet } from "react-router";
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
import SubscriptionPlansPage from "./routes/dashboard/subscription-plans";
import SubscriptionPlanCreateRoute from "./routes/dashboard/subscription-plan-create";
import SubscriptionPlanDetailRoute from "./routes/dashboard/subscription-plan-detail";
import SubscriptionPlanPriceCreateRoute from "./routes/dashboard/subscription-plan-price-create";
import SubscriptionPlanEditRoute from "./routes/dashboard/subscription-plan-edit";
import SubscriptionDiscountsRoute from "./routes/dashboard/subscription-discounts";
import SubscriptionBillingProfileRoute from "./routes/dashboard/subscription-billing-profile";
import SubscriptionMerchantProfileRoute from "./routes/dashboard/subscription-merchant-profile";
import SubscriptionInvoicesRoute from "./routes/dashboard/subscription-invoices";
import SubscriptionSimulationRoute from "./routes/dashboard/subscription-simulation";
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
} from "@seliseblocks/genesis-os";
import {
  DashboardRoute
} from "@seliseblocks/genesis-os/layouts";
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
                  {
                    path: "subscription/plans",
                    element: <SubscriptionPlansPage />,
                  },
                  {
                    path: "subscription/plans/create",
                    element: <SubscriptionPlanCreateRoute />,
                  },
                  {
                    path: "subscription/discounts",
                    element: <SubscriptionDiscountsRoute />,
                  },
                  {
                    path: "subscription/billing-profile",
                    element: <SubscriptionBillingProfileRoute />,
                  },
                  {
                    path: "subscription/merchant-profile",
                    element: <SubscriptionMerchantProfileRoute />,
                  },
                  {
                    path: "subscription/invoices",
                    element: <SubscriptionInvoicesRoute />,
                  },
                  {
                    path: "subscription/plans/:planId",
                    element: <SubscriptionPlanDetailRoute />,
                  },
                  {
                    path: "subscription/plans/:planId/edit",
                    element: <SubscriptionPlanEditRoute />,
                  },
                  {
                    path: "subscription/plans/:planId/prices/create",
                    element: <SubscriptionPlanPriceCreateRoute />,
                  },
                  {
                    path: "subscription/simulation",
                    element: <SubscriptionSimulationRoute />,
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
