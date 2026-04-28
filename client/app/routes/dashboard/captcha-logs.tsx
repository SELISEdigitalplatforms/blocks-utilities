import { Navigate } from "react-router-dom";

export default function CaptchaLogsPage() {
  return <Navigate to="/services/secret-management?tab=captcha" replace />;
}
