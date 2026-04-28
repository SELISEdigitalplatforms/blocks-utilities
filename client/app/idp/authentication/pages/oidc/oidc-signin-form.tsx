import { Form, FormControl, FormField, FormItem, FormLabel, FormMessage } from "@/components/ui-kits/form/form";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Input } from "@/components/ui-kits/input/input";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui-kits/button/button";
import { z } from "zod";
import { useAuthStore } from "@/store/useAuthStore";
import { showErrorToast } from "@/hooks/use-toast";
import { useNavigate } from "react-router-dom";
import { useState } from "react";
import { signinFormDefaultValue, signinFormSchema } from "../login/schema";
import { useOIDCContext } from "@/layouts/oidc-layout";
import { ISigninByEmailPayload, ISigninByEmailResponse } from "@blocks-idp/authentication/models/auth.model";
import { buildOIDCNavigationUrl, getCurrentOIDCParams } from "@blocks-idp/authentication/utils/oidc-utils";
import { PasswordInput } from "@/components/password-input";
import { getApiUrl } from "@/lib/get-api-path";

export const signinByEmail = async (
  payload: ISigninByEmailPayload & { projectKey: string }
): Promise<ISigninByEmailResponse> => {
  try {
    const url = getApiUrl("idp/v1", "Authentication/Login");

    const headers: Record<string, string> = {
      "Content-Type": "application/json",
      accept: "*/*",
    };

    if (payload.projectKey) {
      headers["X-Blocks-Key"] = payload.projectKey;
    }

    const body = JSON.stringify({
      username: payload.username,
      password: payload.password,
      clientId: payload.clientId,
      redirectUri: payload.redirectUri,
      scope: payload.scope,
      state: payload.state,
      nonce: "",
    });

    const response = await fetch(url, {
      method: "POST",
      headers,
      body,
      credentials: "include",
      referrerPolicy: "no-referrer",
    });

    if (!response.ok) {
      const errorText = await response.text();
      let errorJSON: { error?: string; error_description?: string } | null = null;
      try {
        errorJSON = JSON.parse(errorText);
      } catch {
        // not JSON
      }
      if (errorJSON?.error) {
        throw errorJSON;
      }
      throw new Error(`HTTP ${response.status}: ${response.statusText}`);
    }

    const text = await response.text();

    if (!text || text.trim() === "") {
      console.warn("Empty response from signin API, continuing with flow");
      return { access_token: "authenticated" } as ISigninByEmailResponse;
    }

    const parsed = JSON.parse(text);
    return parsed;
  } catch (error) {
    console.error("[Signin By Email] Error:", error);
    throw error;
  }
};

export const OidcSigninForm = () => {
  const { themeColor, projectKey, clientId, scope, state, redirectUri, nonce } = useOIDCContext();
  const navigate = useNavigate();
  const { setAuthenticated } = useAuthStore();
  const [isPending, setIsPending] = useState(false);

  const form = useForm({
    defaultValues: signinFormDefaultValue,
    resolver: zodResolver(signinFormSchema),
  });

  const onSubmitHandler = async (values: z.infer<typeof signinFormSchema>) => {
    try {
      if (!projectKey) {
        showErrorToast({ errors: "Project key is required" });
        return;
      }

      setIsPending(true);
      const res = await signinByEmail({
        username: values.username,
        password: values.password,
        projectKey,
        ...(clientId && { clientId }),
        ...(scope && { scope }),
        ...(state && { state }),
        ...(redirectUri && { redirectUri }),
        ...(nonce && { nonce }),
      });

      if (res.enable_mfa) {
        return navigate(buildOIDCNavigationUrl(`/mfa-check?mfa_id=${res.mfaId}&mfa_type=${res.mfaType}`));
      }

      try {
        localStorage.setItem("oidc-auth-storage", JSON.stringify(res));
      } catch (e) {
        console.error("Failed to save token response to localStorage", e);
      }

      setAuthenticated();

      const params = getCurrentOIDCParams();
      // Add userName from the form to the params
      params.set("userName", values.username);

      const permissionUrl = `/oidc/permission?${params.toString()}`;
      navigate(permissionUrl);
    } catch (_error: unknown) {
      const apiError = _error as { error?: string; error_description?: string };
      const baseUrl = buildOIDCNavigationUrl(`/oidc/error`);
      const errorParams = new URLSearchParams();
      if (apiError?.error) errorParams.set("error", apiError.error);
      if (apiError?.error_description) errorParams.set("error_description", apiError.error_description);
      const errorQuery = errorParams.toString();
      const separator = baseUrl.includes("?") ? "&" : "?";
      navigate(errorQuery ? `${baseUrl}${separator}${errorQuery}` : baseUrl);
    } finally {
      setIsPending(false);
    }
  };

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(onSubmitHandler)} className="flex flex-col gap-4">
        <FormField
          control={form.control}
          name="username"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Email</FormLabel>
              <FormControl>
                <Input placeholder="Enter your email" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={form.control}
          name="password"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Password</FormLabel>
              <FormControl>
                <PasswordInput placeholder="Enter your password" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <Link
          to={buildOIDCNavigationUrl("/oidc/forgot-password")}
          className="ml-auto inline-block text-sm hover:underline"
          style={{ color: themeColor }}
        >
          Forgot password?
        </Link>

        <Button
          type="submit"
          className="w-full rounded hover:opacity-90"
          style={{
            backgroundColor: themeColor,
          }}
          disabled={isPending}
        >
          Log in
        </Button>
      </form>
    </Form>
  );
};
