import { showErrorToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useAuthStore } from "@/store/useAuthStore";
import { useSigninBySSO } from "@blocks-idp/authentication/hooks/use-auth";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useEffect, useRef } from "react";

const SSO_GUARD_PREFIX = "sso_activated_";

/** Returns true if this state has already been consumed. Sets the guard if not. */
function acquireGuard(state: string): boolean {
  const key = `${SSO_GUARD_PREFIX}${state}`;
  try {
    if (sessionStorage.getItem(key)) return true;
    sessionStorage.setItem(key, "1");
  } catch {
    // sessionStorage unavailable — fall through without guard
  }
  return false;
}

/** Removes the guard so the user can retry after a genuine error. */
function releaseGuard(state: string): void {
  try {
    sessionStorage.removeItem(`${SSO_GUARD_PREFIX}${state}`);
  } catch {
    /* noop */
  }
}

function handleSsoError(error: unknown): void {
  const errorStr = JSON.stringify(error);
  if (errorStr.includes("user_not_found")) {
    const errorObj = error as any;
    const description = errorObj?.error?.description || errorObj?.description || "";
    const firstWord = description.split(" ")[0];
    const emailTarget = firstWord.includes("@") ? firstWord : "";
    const msg = `There is no account with this email${emailTarget ? ` (${emailTarget})` : ""}.`;
    showErrorToast({ errors: msg });
  } else if (errorStr.includes("state_data_not_found")) {
    showErrorToast({ errors: "Something went wrong." });
  } else if (isErrorWithErrors(error)) {
    showErrorToast({ errors: error.errors });
  } else {
    showErrorToast({ errors: "Something went wrong" });
  }
}

function getSsoActivationPath(url: string): string | null {
  const queryPart = url.split("?")[1];
  if (!queryPart) return null;

  const params = new URLSearchParams(queryPart);
  const username = params.get("username");
  const ssoCode = params.get("code");

  return username && ssoCode ? `/sso-activate?username=${username}&code=${ssoCode}` : null;
}

/**
 * Reads `code` and `state` from the URL search params and exchanges them
 * for a session token. Handles deduplication (Apple POST → 302 → GET),
 * MFA redirects, and error feedback.
 */
export function useSsoActivation() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const { setAuthenticated } = useAuthStore();
  const { mutateAsync, isPending, reset } = useSigninBySSO();

  const code = searchParams.get("code");
  const state = searchParams.get("state");
  const effectRan = useRef(false);

  useEffect(() => {
    if (!code || !state) return;
    if (effectRan.current) return;

    if (acquireGuard(state)) {
      return;
    }

    effectRan.current = true;

    async function activate() {
      try {
        const res = await mutateAsync({ code: code as string, state: state as string });

        const activationPath = res.sso_user_redirect_url ? getSsoActivationPath(res.sso_user_redirect_url) : null;
        if (activationPath) return navigate(activationPath);

        if (res.enable_mfa) return navigate(`/mfa-check?mfa_id=${res.mfaId}&mfa_type=${res.mfaType}`);

        setAuthenticated();
        navigate("/console");
      } catch (error) {
        releaseGuard(state as string);
        reset();
        handleSsoError(error);
        navigate("/login");
        /**
         * * Important: use replace, not push. After an SSO redirect, the user lands on /login with code+state in the URL. If we push a new entry, the user can hit Back and return to /login with the same code+state, which will cause the guard to block them without any feedback. By replacing the URL, we ensure that hitting Back takes them to the page before /login, not back to /login itself.
         */
      }
    }

    activate();
  }, [code, state, mutateAsync, navigate, setAuthenticated, reset]);

  return { isPending };
}
