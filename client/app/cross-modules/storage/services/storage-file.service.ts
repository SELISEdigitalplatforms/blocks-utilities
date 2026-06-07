import { HttpClient } from "@/lib/http-client";
import {
  IDeleteFilePayload,
  IDeleteFolderPayload,
  IDeleteResourceResponse,
  IGetFileByFileIDPayload,
  IGetFileByFileIDResponse,
  IGetFilesInfoPayload,
  IGetFilesInfoResponse,
  IGetPreSignedUrlForUploadPayload,
  IGetPreSignedUrlForUploadResponse,
  IUpdateFileAdditionalInfoPayload,
  IUpdateFileAdditionalInfoResponse,
} from "../models/storage.model";
import { STORAGE_CONFIG_ENDPOINTS } from "../constants/endpoint.constant";
import { deriveLogicBaseUrl } from "@/lib/blocks-url.util";
import { getRuntimeEnv } from "@/lib/runtime-env";

const logicHttp = new HttpClient(
  deriveLogicBaseUrl(),
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export class StorageFile {
  getFileByFileId(
    payload: IGetFileByFileIDPayload,
  ): Promise<IGetFileByFileIDResponse> {
    return logicHttp.get(
      `${STORAGE_CONFIG_ENDPOINTS.GET_FILE}?FileId=${payload.itemId}&ProjectKey=${payload.projectKey}&ConfigurationName=${payload.configurationName ?? ""}`,
    );
  }

  deleteFileByFileId(
    payload: IDeleteFilePayload,
  ): Promise<IDeleteResourceResponse> {
    return logicHttp.post(STORAGE_CONFIG_ENDPOINTS.DELETE_FILE, payload);
  }

  deleteFolderByFileId(
    payload: IDeleteFolderPayload,
  ): Promise<IDeleteResourceResponse> {
    return logicHttp.post(STORAGE_CONFIG_ENDPOINTS.DELETE_FOLDER, payload);
  }

  getPreSignedUrlForUpload(
    payload: IGetPreSignedUrlForUploadPayload,
  ): Promise<IGetPreSignedUrlForUploadResponse> {
    return logicHttp.post(STORAGE_CONFIG_ENDPOINTS.GET_PRESIGNED_URL, payload);
  }

  getFilesInfoUrlForUpload(
    payload: IGetFilesInfoPayload,
  ): Promise<IGetFilesInfoResponse> {
    return logicHttp.post(STORAGE_CONFIG_ENDPOINTS.GET_FILES_INFO, payload);
  }

  updateFileAdditionalInfo(
    payload: IUpdateFileAdditionalInfoPayload,
  ): Promise<IUpdateFileAdditionalInfoResponse> {
    return logicHttp.post(
      STORAGE_CONFIG_ENDPOINTS.UPDATE_FILE_ADDITIONAL_INFO,
      payload,
    );
  }

  getFilesDownloadUrl(meta: {
    fileId: string;
    projectKey: string;
  }): Promise<IGetFileByFileIDResponse> {
    return logicHttp.get(
      `${STORAGE_CONFIG_ENDPOINTS.GET_FILE}?FileId=${meta.fileId}&ProjectKey=${meta.projectKey}`,
    );
  }
}
