import { Navigate } from "react-router-dom";

export default function MfaLogsPage() {
  return <Navigate to="/services/secret-management?tab=mfa" replace />;
}
