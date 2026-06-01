import { createBrowserRouter, Navigate } from "react-router-dom";
import { useAuthStore } from "./store/useAuthStore";

import { AuthLayout } from "./layouts/auth-layout";
import { PublicLayout } from "./layouts/public-layout";
import { OidcLayout } from "./layouts/oidc-layout";
import { DashboardLayout } from "./layouts/dashboard-layout";
import { ConsoleLayout } from "./layouts/console-layout";

// Auth routes (public, with auth layout)
import LoginPage from "./routes/auth/login";
import LoginSimplePage from "./routes/auth/login-simple";
import SignupPage from "./routes/auth/signup";
import SsoActivatePage from "./routes/auth/sso-activate";

// Public routes (with public guard only)
import ActivatePage from "./routes/auth/activate";
import ForgotPasswordPage from "./routes/auth/forgot-password";
import ResetPasswordPage from "./routes/auth/resetpassword";
import ActivateSuccessPage from "./routes/auth/activate-success";
import ForgotEmailSentPage from "./routes/auth/forgot-email-sent";
import SignupEmailSentPage from "./routes/auth/signup-email-sent";
import MfaCheckPage from "./routes/auth/mfa-check";
import ResetPasswordSuccessPage from "./routes/auth/reset-password-success";

// OIDC routes (un-guarded)
import OidcIndexPage from "./routes/oidc/index";
import OidcLoginPage from "./routes/oidc/login";
import OidcPermissionPage from "./routes/oidc/permission";
import OidcErrorPage from "./routes/oidc/error";
import OidcForgotPasswordPage from "./routes/oidc/forgot-password";
import OidcEmailSentConfirmationPage from "./routes/oidc/email-sent-confirmation";

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

// Console pages
import { Console } from "./pages/console/console";
import { CreateProjectWrapper } from "./pages/create-project/create-project";
import CallbackPage from "./routes/callback/callback";

// Project overview routes
import { ProjectOverviewLayout } from "./layouts/project-overview-layout";
import { EnvironmentsPage } from "./pages/environments/environments";
import { PeopleManagement } from "./pages/people/people-management";
import { RepositoriesPage } from "./pages/repositories/repositories";
import { SettingsPage } from "./pages/settings/settings";
import LoginCallbackPage from "./routes/auth/callback";

function RootRedirect() {
  const { isAuthenticated } = useAuthStore();
  if (isAuthenticated) {
    return <Navigate to="/email" replace />;
  }
  return <Navigate to="/login" replace />;
}

export const router = createBrowserRouter([
  // ── Auth layout (login, signup, sso-activate) ──
  {
    element: <AuthLayout />,
    children: [
      { path: "/login-classic", element: <LoginPage /> },
      { path: "/signup", element: <SignupPage /> },
      { path: "/sso-activate", element: <SsoActivatePage /> },
    ],
  },

  // ── Simple login (no guards, no API calls) ──
  {
    path: "/login",
    children: [
      { index: true, element: <LoginSimplePage /> },
      { path: "callback", element: <LoginCallbackPage /> },
    ],
  },

  // ── Public layout (other public pages with PublicGuard) ──
  {
    element: <PublicLayout />,
    children: [
      { path: "/activate", element: <ActivatePage /> },
      { path: "/forgot-password", element: <ForgotPasswordPage /> },
      { path: "/resetpassword", element: <ResetPasswordPage /> },
      { path: "/activate-success", element: <ActivateSuccessPage /> },
      { path: "/forgot-email-sent", element: <ForgotEmailSentPage /> },
      { path: "/signup-email-sent", element: <SignupEmailSentPage /> },
      { path: "/mfa-check", element: <MfaCheckPage /> },
      {
        path: "/reset-password-success",
        element: <ResetPasswordSuccessPage />,
      },
    ],
  },

  // ── OIDC layout (un-guarded, themed) ──
  {
    path: "/oidc",
    element: <OidcLayout />,
    children: [
      { index: true, element: <OidcIndexPage /> },
      { path: "login", element: <OidcLoginPage /> },
      { path: "permission", element: <OidcPermissionPage /> },
      { path: "error", element: <OidcErrorPage /> },
      { path: "forgot-password", element: <OidcForgotPasswordPage /> },
      {
        path: "email-sent-confirmation",
        element: <OidcEmailSentConfirmationPage />,
      },
    ],
  },

  // ── Dashboard layout (protected routes) ──
  {
    element: <DashboardLayout />,
    children: [
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
      // { path: "/project-overview/people", element: <PeopleManagement /> },
      // { path: "/project-overview/repositories", element: <RepositoriesPage /> },
      // { path: "/project-overview/settings", element: <SettingsPage /> },
    ],
  },

  // ── Console layout (profile, console pages, project selection) ──
  {
    element: <ConsoleLayout />,
    children: [
      { path: "/profile", element: <ProfilePage /> },
      { path: "/console", element: <Console /> },
      { path: "/create-project", element: <CreateProjectWrapper /> },
      { path: "/callback", element: <CallbackPage /> },
    ],
  },

  // ── Root redirect: check auth state first, then redirect appropriately ──
  { path: "/", element: <RootRedirect /> },

  // ── Catch-all: redirect to login ──
  { path: "*", element: <Navigate to="/login" replace /> },
]);
