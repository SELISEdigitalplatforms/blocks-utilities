import { authHandlers } from "./authHandler";
import { iamHandlers } from "./iamHandler";
import { captchaHandlers } from "./captchaHandler";
import { mfaHandlers } from "./mfaHandler";

/**
 * Aggregated IDP MSW handlers across all domains:
 * - Authentication (signin, signup, SSO, OIDC, client credentials, auth config)
 * - IAM (users, accounts, roles, permissions, organizations, configuration)
 * - Captcha (configuration management)
 * - MFA (configuration, OTP generation, TOTP setup, verification)
 */
export const idpHandlers = [...authHandlers, ...iamHandlers, ...captchaHandlers, ...mfaHandlers];
