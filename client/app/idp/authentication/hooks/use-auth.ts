import { useMutation, useQuery } from "@tanstack/react-query";
import { authService } from "@blocks-idp/authentication/services/auth.service";
import { oauthService } from "@blocks-idp/authentication/services/oauth.service";

export const useSigninByEmail = () => {
  return useMutation({
    mutationKey: ["login", "email"],
    mutationFn: authService.signinByEmail,
  });
};

export const useSigninBySSO = () => {
  return useMutation({
    mutationKey: ["login", "sso"],
    mutationFn: oauthService.signinBySSO,
  });
};

export const useVerifyMfa = () => {
  return useMutation({
    mutationKey: ["verify", "mfa"],
    mutationFn: authService.verifyMfa,
  });
};

export const useLogout = () => {
  return useMutation({
    mutationKey: ["logout"],
    mutationFn: authService.logout,
  });
};

export const useSignupByEmail = () => {
  return useMutation({
    mutationKey: ["signup", "email"],
    mutationFn: authService.signupByEmail,
  });
};

export const useGetLoginOptions = () => {
  return useQuery({
    queryKey: ["login-options"],
    queryFn: () => authService.getLoginOptions(),
  });
};
