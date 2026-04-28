import { useProjectStore } from "@/store/useProjectStore";
import { IEmailTemplate } from "@blocks-communication/mail/models/email";
import { emailService } from "@blocks-communication/mail/services/email.services";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

export const useSendTestMail = () => {
  return useMutation({
    mutationKey: ["test-mail", "send"],
    mutationFn: emailService.sendTestMail,
    onSuccess: () => {},
  });
};

export const useCloneTemplate = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["template", "clone"],
    mutationFn: emailService.cloneMailTemplate,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["email-templates"] });
    },
  });
};

export const useDeleteEmailTemplate = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["template", "delete"],
    mutationFn: emailService.deleteMailTemplate,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["email-templates"] });
    },
  });
};

export const useGetEmailTemplates = (
  pageNumber: number,
  pageSize: number,
  searchKey: string,
  sortProperty: string,
  isDescending: boolean,
  language: string,
  mailConfigurationId: string,
) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  return useQuery({
    queryKey: [
      "email-templates",
      tenantId,
      pageNumber,
      pageSize,
      searchKey,
      sortProperty,
      isDescending,
      language,
      mailConfigurationId,
    ],
    staleTime: 0,
    queryFn: () =>
      emailService.fetchEmailTemplates(
        pageNumber,
        pageSize,
        tenantId,
        searchKey,
        sortProperty,
        isDescending,
        language,
        mailConfigurationId,
      ),
  });
};

export const useGetEmailTemplate = (itemId: string) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  return useQuery({
    queryKey: ["email-template", tenantId, itemId],
    staleTime: 0,
    queryFn: () => emailService.fetchEmailTemplate(tenantId, itemId),
  });
};

export const useSaveEmailTemplate = () => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  const [isPending, setIsPending] = useState<boolean>(false);

  const saveEmailTemplate = async (data: IEmailTemplate) => {
    try {
      setIsPending(true);
      const payload = {
        ...data,
        itemId: data.itemId ? data.itemId : "",
        projectKey: tenantId,
      };
      const response = await emailService.saveMailTemplate(payload);
      setIsPending(false);
      return response;
    } catch (error) {
      setIsPending(false);
      throw error;
    }
  };
  return { saveEmailTemplate, isPending };
};
export const useSaveMailTemplate = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["template", "save"],
    mutationFn: emailService.saveMailTemplate,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["email-templates"] });
      queryClient.invalidateQueries({ queryKey: ["email-template"] });
    },
  });
};
