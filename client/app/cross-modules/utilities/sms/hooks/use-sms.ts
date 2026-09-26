import { useProjectStore } from "@seliseblocks/genesis-os";
import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import {
  SMS_PROVIDER_QUERY_KEY,
  SMS_TEMPLATES_QUERY_KEY,
} from "../constants/sms.constants";
import type {
  SaveSmsProviderConfigurationRequest,
  SaveSmsTemplateRequest,
  SendSmsByTemplateRequest,
  SendSmsRequest,
  SmsTemplateQuery,
} from "../models/sms.model";
import { smsService } from "../services/sms.service";

// Tenant in every key: switching project must never show the previous project's data.
const useTenantId = () => useProjectStore()?.selectedProject?.tenantId ?? "";

export const useSmsProviderConfiguration = () => {
  const tenantId = useTenantId();

  return useQuery({
    queryKey: [SMS_PROVIDER_QUERY_KEY, tenantId],
    queryFn: () => smsService.getProviderConfiguration(),
    staleTime: 30_000,
  });
};

export const useSaveSmsProviderConfiguration = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: SaveSmsProviderConfigurationRequest) =>
      smsService.saveProviderConfiguration(request),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: [SMS_PROVIDER_QUERY_KEY] }),
  });
};

export const useSmsTemplates = (query: SmsTemplateQuery) => {
  const tenantId = useTenantId();

  return useQuery({
    queryKey: [SMS_TEMPLATES_QUERY_KEY, tenantId, query],
    queryFn: () => smsService.getTemplates(query),
    placeholderData: keepPreviousData,
  });
};

export const useSaveSmsTemplate = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: SaveSmsTemplateRequest) => smsService.saveTemplate(request),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: [SMS_TEMPLATES_QUERY_KEY] }),
  });
};

export const useDeleteSmsTemplate = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (templateId: string) => smsService.deleteTemplate(templateId),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: [SMS_TEMPLATES_QUERY_KEY] }),
  });
};

export const useSendSms = () =>
  useMutation({
    mutationFn: (request: SendSmsRequest) => smsService.send(request),
  });

export const useSendSmsByTemplate = () =>
  useMutation({
    mutationFn: (request: SendSmsByTemplateRequest) => smsService.sendByTemplate(request),
  });
