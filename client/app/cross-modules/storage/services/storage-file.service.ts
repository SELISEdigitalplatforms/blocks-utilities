import { HttpClient } from "@/lib/http-client";
import { getRuntimeEnv } from "@/lib/runtime-env";
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
import { STORAGE_FILE_ENDPOINTS } from "../constants/endpoint.constant";

const storageHttp = new HttpClient(
  getRuntimeEnv("BLOCKS_LOGIC_BASE_URL") || "",
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export class StorageFile {
  getFileByFileId(
    payload: IGetFileByFileIDPayload,
  ): Promise<IGetFileByFileIDResponse> {
    return storageHttp.get(
      `${STORAGE_FILE_ENDPOINTS.GET_FILE}?FileId=${payload.itemId}&ProjectKey=${payload.projectKey}&ConfigurationName=${payload.configurationName ?? ""}`,
    );
  }

  deleteFileByFileId(
    payload: IDeleteFilePayload,
  ): Promise<IDeleteResourceResponse> {
    return storageHttp.post(STORAGE_FILE_ENDPOINTS.DELETE_FILE, payload);
  }

  deleteFolderByFileId(
    payload: IDeleteFolderPayload,
  ): Promise<IDeleteResourceResponse> {
    return storageHttp.post(STORAGE_FILE_ENDPOINTS.DELETE_FOLDER, payload);
  }

  getPreSignedUrlForUpload(
    payload: IGetPreSignedUrlForUploadPayload,
  ): Promise<IGetPreSignedUrlForUploadResponse> {
    return storageHttp.post(STORAGE_FILE_ENDPOINTS.GET_PRESIGNED_URL, payload);
  }

  getFilesInfoUrlForUpload(
    payload: IGetFilesInfoPayload,
  ): Promise<IGetFilesInfoResponse> {
    return storageHttp.post(STORAGE_FILE_ENDPOINTS.GET_FILES_INFO, payload);
  }

  updateFileAdditionalInfo(
    payload: IUpdateFileAdditionalInfoPayload,
  ): Promise<IUpdateFileAdditionalInfoResponse> {
    return storageHttp.post(
      STORAGE_FILE_ENDPOINTS.UPDATE_FILE_ADDITIONAL_INFO,
      payload,
    );
  }

  getFilesDownloadUrl(meta: {
    fileId: string;
    projectKey: string;
  }): Promise<IGetFileByFileIDResponse> {
    return storageHttp.get(
      `${STORAGE_FILE_ENDPOINTS.GET_FILE}?FileId=${meta.fileId}&ProjectKey=${meta.projectKey}`,
    );
  }
}
