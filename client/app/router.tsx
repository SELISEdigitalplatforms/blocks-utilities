import { createBrowserRouter, Navigate, Outlet } from "react-router-dom";
import EmailPage from "./routes/dashboard/email";
import NewCommunicationPage from "./routes/dashboard/new-communication";
import EmailCommunicationDetailsPage from "./routes/dashboard/email-communication-details";
import EmailTemplateEditPage from "./routes/dashboard/email-template-edit";
import EmailUsageDetailsPage from "./routes/dashboard/email-usage-details";
import NotificationPage from "./routes/dashboard/notification";
import MagicUrlPage from "./routes/dashboard/magic-url";
import MagicUrlDetailsPage from "./routes/dashboard/magic-url-details";
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
                  { path: "email", element: <EmailPage /> },
                  {
                    path: "new-communication",
                    element: <NewCommunicationPage />,
                  },
                  {
                    path: "email/communications/:id",
                    element: <EmailCommunicationDetailsPage />,
                  },
                  {
                    path: "email/communications/:id/edit",
                    element: <EmailTemplateEditPage />,
                  },
                  {
                    path: "email/usage/:id",
                    element: <EmailUsageDetailsPage />,
                  },
                  { path: "notification", element: <NotificationPage /> },
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
