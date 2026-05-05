import { http } from "@/lib/http-client";
import {
  IGetMagicUrlByIdPayload,
  IGetMagicUrlsPayload,
  IGetMagicUrlsResponse,
  MagicUrl,
  ICreateMagicUrlPayload,
} from "@blocks-utilities/magic-url/models/magic-url.model";
import {
  ISaveMagicUrlConfigPayload,
  ISaveMagicUrlConfigResponse,
} from "@blocks-utilities/magic-url/models/magic-url-config.model";
import { IAPIResponse } from "@/models/api-response";
import { MAGIC_URL_ENDPOINTS } from "@blocks-utilities/magic-url/constants/endpoint.constant";

export class MagicUrlService {
  async getMagicUrl(payload: IGetMagicUrlByIdPayload): Promise<MagicUrl> {
    try {
      const { ItemId, projectKey } = payload;
      const response = await http.get<IAPIResponse<MagicUrl>>(
        `${MAGIC_URL_ENDPOINTS.GET_LINK}?ItemId=${ItemId}&ProjectKey=${projectKey}`,
      );
      return response.data;
    } catch (error) {
      console.error("Failed to fetch Magic URL details:", error);
      throw error;
    }
  }

  async getMagicUrls(
    payload: IGetMagicUrlsPayload,
  ): Promise<IGetMagicUrlsResponse> {
    try {
      const {
        page,
        pageSize,
        projectKey,
        searchText,
        status,
        expiryDateRangeStartDate,
        expiryDateRangeEndDate,
        requestMethod,
        type,
      } = payload;

      const params = new URLSearchParams({
        PageSize: pageSize.toString(),
        PageNumber: page.toString(),
        ProjectKey: projectKey,
      });

      if (searchText) params.append("SearchText", searchText);
      if (status) params.append("Status", status);
      if (requestMethod) params.append("RequestMethod", requestMethod);
      if (type) params.append("Type", type);
      if (expiryDateRangeStartDate)
        params.append("ExpiryDateRange.StartDate", expiryDateRangeStartDate);
      if (expiryDateRangeEndDate)
        params.append("ExpiryDateRange.EndDate", expiryDateRangeEndDate);

      const response = await http.get<IAPIResponse<MagicUrl[]>>(
        `${MAGIC_URL_ENDPOINTS.GET_LINKS}?${params.toString()}`,
      );

      return {
        data: response.data,
        errors: response.errors ?? [],
        totalCount: response.totalCount ?? 0,
      };
    } catch (error) {
      console.error("Failed to fetch URL shorteners:", error);
      throw error;
    }
  }

  async createMagicUrl(payload: ICreateMagicUrlPayload): Promise<MagicUrl> {
    try {
      const response = await http.post<MagicUrl>(
        MAGIC_URL_ENDPOINTS.CREATE_LINK,
        payload,
      );
      return response;
    } catch (error) {
      console.error("Failed to create short URL:", error);
      throw error;
    }
  }

  async saveMagicUrlConfig(
    payload: ISaveMagicUrlConfigPayload,
  ): Promise<ISaveMagicUrlConfigResponse> {
    try {
      const response = await http.post<ISaveMagicUrlConfigResponse>(
        MAGIC_URL_ENDPOINTS.SAVE_CONFIG,
        payload,
      );
      return response;
    } catch (error) {
      console.error("Failed to save Magic URL config:", error);
      throw error;
    }
  }

  async getMagicUrlConfig(
    projectKey: string,
  ): Promise<ISaveMagicUrlConfigResponse> {
    try {
      const response = await http.get<ISaveMagicUrlConfigResponse>(
        `${MAGIC_URL_ENDPOINTS.GET_CONFIG}?ProjectKey=${projectKey}`,
      );
      return response;
    } catch (error) {
      console.error("Failed to fetch Magic URL config:", error);
      throw error;
    }
  }

  async deactivateMagicLinks(payload: {
    linkIds: string[];
    projectKey: string;
  }): Promise<void> {
    try {
      await http.post(MAGIC_URL_ENDPOINTS.REMOVE_LINKS, payload);
    } catch (error) {
      console.error("Failed to remove short URL:", error);
      throw error;
    }
  }
}

export const magicUrlService = new MagicUrlService();
