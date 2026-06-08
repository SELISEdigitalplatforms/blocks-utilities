import { createBrowserRouter, Navigate, Outlet } from "react-router-dom";
import { useAuthStore } from "./store/useAuthStore";

import { DashboardLayout } from "./layouts/dashboard-layout";

// Dashboard routes (protected)
import AuthenticationConfigPage from "./routes/dashboard/authentication-config";
import SsoConfigurationPage from "./routes/dashboard/sso-configuration";
import EmailPage from "./routes/dashboard/email";
import NewCommunicationPage from "./routes/dashboard/new-communication";
import EmailCommunicationDetailsPage from "./routes/dashboard/email-communication-details";
import EmailTemplateEditPage from "./routes/dashboard/email-template-edit";
import EmailUsageDetailsPage from "./routes/dashboard/email-usage-details";
import NotificationPage from "./routes/dashboard/notification";
import MagicUrlPage from "./routes/dashboard/magic-url";
import MagicUrlDetailsPage from "./routes/dashboard/magic-url-details";
import ProfilePage from "./routes/dashboard/profile";


import {
  AuthResolver,
  PublicGuard,
  LoginPage,
  ProtectedGuard,
  ConsoleLayout,
  ImpersonationChecker,
  ImpersonationTerminator,
  ImpersonationSynchronizer,
  ConsolePage,
  CallbackPage,
  ProjectOverviewLayout,
} from "@seliseblocks/blocks-kit";

// Project overview routes
import { EnvironmentsPage } from "./pages/environments/environments";
import LoginSimplePage from "./routes/auth/login-simple";

function RootRedirect() {
  const { isAuthenticated } = useAuthStore();
  if (isAuthenticated) {
    return <Navigate to="/email" replace />;
  }
  return <Navigate to="/login" replace />;
}

export const router = createBrowserRouter([
  {
    element: <Outlet />,
    children: [
      // All Redirect Url Handle here
      {
        element: <Outlet />,
        children: [
          {
            path: "/login/callback",
            element: <CallbackPage redirectUrl="/console" />,
          },
        ],
      },
      {
        // Set User Auth Information and resolve authentication state before rendering any route
        element: (
          <AuthResolver>
            <Outlet />
          </AuthResolver>
        ),
        children: [
          // publuc
          {
            element: (
              <PublicGuard>
                <Outlet />
              </PublicGuard>
            ),
            children: [{ path: "/login", element: <LoginPage name="blocks-utilities"/> }],
          },

          // protected
          {
            element: (
              <ProtectedGuard>
                <Outlet />
              </ProtectedGuard>
            ),
            children: [
              {
                element: (
                  <ImpersonationChecker>
                    <ImpersonationTerminator>
                      <Outlet/>
                    </ImpersonationTerminator>
                  </ImpersonationChecker>
                ),
                children: [
                  {
                    element: (
                      <ConsoleLayout>
                        <Outlet />
                      </ConsoleLayout>
                    ),
                    children: [
                      { path: "/profile", element: <ProfilePage /> },
                      { path: "/console", element: <ConsolePage /> },
                    ],
                  },
                  {
                    element: <ProjectOverviewLayout />,
                    children: [
                      {
                        path: "/project-overview/environments",
                        element: <EnvironmentsPage />,
                      },
                    ],
                  },
                ],
              },
              {
                // impersonate
                element: (
                  <ImpersonationChecker>
                    <ImpersonationSynchronizer>
                      <DashboardLayout />
                    </ImpersonationSynchronizer>
                  </ImpersonationChecker>
                ),
                children: [
                  { path: "/dashboard", element: <EmailPage /> },
                  
                  {
                    path: "/services/authentication",
                    element: <AuthenticationConfigPage />,
                  },
                  {
                    path: "/services/authentication/sso-configuration",
                    element: <SsoConfigurationPage />,
                  },
                  { path: "/email", element: <EmailPage /> },
                  { path: "/new-communication", element: <NewCommunicationPage /> },
                  {
                    path: "/email/communications/:id",
                    element: <EmailCommunicationDetailsPage />,
                  },
                  {
                    path: "/email/communications/:id/edit",
                    element: <EmailTemplateEditPage />,
                  },
                  { path: "/email/usage/:id", element: <EmailUsageDetailsPage /> },
                  { path: "/notification", element: <NotificationPage /> },
                  { path: "/magic-url", element: <MagicUrlPage /> },
                  { path: "/magic-url/details/:id", element: <MagicUrlDetailsPage /> },
                  {
                    path: "/project-overview",
                    element: <Navigate to="/project-overview/environments" replace />,
                  },
                  { path: "/project-overview/environments", element: <EnvironmentsPage /> },
                
                ],
              },
            ],
          },
        ],
      },
      {
        path: "*",
        element: <Navigate to="/console" replace />,
      },
    ],
  },
]);