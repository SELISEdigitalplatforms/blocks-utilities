import { useQuery } from "@tanstack/react-query";
import { emailService } from "@blocks-communication/mail/services/email.services";
import { useProjectStore } from "@/store/useProjectStore";
import { EMAIL_USAGE_REFETCH_INTERVAL } from "@blocks-communication/mail/constants/email-usage";

export const useGetEmailUsage = (
  page: number,
  pageSize: number,
  isInbound: boolean,
  searchText?: string,
  status?: string,
  startDate?: string,
  endDate?: string,
) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  return useQuery({
    queryKey: [
      "email-usage",
      page,
      pageSize,
      isInbound,
      searchText,
      status,
      startDate,
      endDate,
      tenantId,
    ],
    queryFn: async () => {
      if (!tenantId) return { data: [], totalCount: 0 };
      const response = await emailService.getMailBoxMails(
        tenantId,
        page,
        pageSize,
        isInbound,
        searchText,
        status,
        startDate,
        endDate,
      );
      return {
        data: response.mails,
        totalCount: response.totalCount,
      };
    },
    enabled: !!tenantId,
    refetchInterval: EMAIL_USAGE_REFETCH_INTERVAL,
  });
};

export const useGetEmailUsageById = (id: string) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  return useQuery({
    queryKey: ["email-usage-details", id, tenantId],
    queryFn: async () => {
      if (!tenantId) return null;
      const response = await emailService.getMailBoxMail(tenantId, id);
      return response.mail;
    },
    enabled: !!tenantId && !!id,
  });
};
