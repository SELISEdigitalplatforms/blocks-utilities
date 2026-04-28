import { userService } from "@blocks-idp/iam/services/user.service";
import { useMutation, useQueryClient } from "@tanstack/react-query";

export const useAccountActivation = () => {
  return useMutation({
    mutationKey: ["account", "activation"],
    mutationFn: userService.account.accountActivation,
  });
};

export const useAccountResendActivation = () => {
  return useMutation({
    mutationKey: ["account", "resend-activation"],
    mutationFn: userService.account.accountResendActivation,
  });
};

export const useAccountDeactivate = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["account", "deactivate"],
    mutationFn: userService.accountDeactivate,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["user"] });
      queryClient.invalidateQueries({ queryKey: ["users"] });
    },
  });
};

export const useAccountRecover = () => {
  return useMutation({
    mutationKey: ["account", "recover"],
    mutationFn: userService.account.accountRecover,
  });
};

export const useAccountResetPassword = () => {
  return useMutation({
    mutationKey: ["account", "reset-password"],
    mutationFn: userService.account.accountResetPassword,
  });
};

export const useAccountActivationCodeExpiration = () => {
  return useMutation({
    mutationKey: ["account", "activation-code-expiration"],
    mutationFn: userService.account.checkActivationCodeExpiration,
  });
};
