import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { mfaService } from "../services/mfa.service";
import { IGetUserByIdPayload } from "@blocks-idp/iam/models/user";

export const useGetMFAConfig = (option: { projectKey: string }) => {
  return useQuery({
    queryKey: ["mfa-config", "get", option.projectKey],
    queryFn: () => mfaService.getConfigurations({ projectKey: option.projectKey }),
  });
};

export const useSaveMFAConfig = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["mfa-config", "save"],
    mutationFn: mfaService.saveMFAConfiguration,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["mfa-config", "get"] });
    },
  });
};

export const useConfigureUserMFA = (option: { id: string; projectKey: string }) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["mfa-config", "configure"],
    mutationFn: mfaService.configureUserMFA,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["user", option] });
    },
  });
};

export const useGetTotp = (option: { id: string; projectKey: string }) => {
  return useQuery({
    queryKey: ["mfa-config", "setup-totp", option],
    queryFn: () => mfaService.setupUserTotp(option),
  });
};

export const useGenerateUserMfaOTP = () => {
  return useMutation({
    mutationKey: ["mfa-config", "generate-otp"],
    mutationFn: mfaService.generateUserMfaOTP,
  });
};

export const useVerifyMfaOTP = (option: IGetUserByIdPayload & { own?: boolean }) => {
  const queryClient = useQueryClient();
  const { own = false, ...rest } = option;
  return useMutation({
    mutationKey: ["mfa-config", "verify-otp"],
    mutationFn: mfaService.verifyOtp,
    onSuccess: () => {
      if (own) return queryClient.invalidateQueries({ queryKey: ["user"] });
      queryClient.invalidateQueries({ queryKey: ["user", rest] });
    },
  });
};

export const useResendMfaOTP = () => {
  return useMutation({
    mutationKey: ["mfa-config", "resend-otp"],
    mutationFn: mfaService.resendOtp,
  });
};
export const useDisableMfa = (option: { id: string; projectKey: string }) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["mfa-config", "disable"],
    mutationFn: mfaService.disableMFA,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["user", option] });
    },
  });
};
