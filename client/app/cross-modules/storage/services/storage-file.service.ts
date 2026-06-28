import { serviceInstances } from "@/lib/http-client";
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

export class StorageFile {
  getFileByFileId(
    payload: IGetFileByFileIDPayload,
  ): Promise<IGetFileByFileIDResponse> {
    return serviceInstances.logicService.get(
      `${STORAGE_CONFIG_ENDPOINTS.GET_FILE}?FileId=${payload.itemId}&ProjectKey=${payload.projectKey}&ConfigurationName=${payload.configurationName ?? ""}`,
    );
  }

  deleteFileByFileId(
    payload: IDeleteFilePayload,
  ): Promise<IDeleteResourceResponse> {
    return serviceInstances.logicService.post(STORAGE_CONFIG_ENDPOINTS.DELETE_FILE, payload);
  }

  deleteFolderByFileId(
    payload: IDeleteFolderPayload,
  ): Promise<IDeleteResourceResponse> {
    return serviceInstances.logicService.post(STORAGE_CONFIG_ENDPOINTS.DELETE_FOLDER, payload);
  }

  getPreSignedUrlForUpload(
    payload: IGetPreSignedUrlForUploadPayload,
  ): Promise<IGetPreSignedUrlForUploadResponse> {
    return serviceInstances.logicService.post(STORAGE_CONFIG_ENDPOINTS.GET_PRESIGNED_URL, payload);
  }

  getFilesInfoUrlForUpload(
    payload: IGetFilesInfoPayload,
  ): Promise<IGetFilesInfoResponse> {
    return serviceInstances.logicService.post(STORAGE_CONFIG_ENDPOINTS.GET_FILES_INFO, payload);
  }

  updateFileAdditionalInfo(
    payload: IUpdateFileAdditionalInfoPayload,
  ): Promise<IUpdateFileAdditionalInfoResponse> {
    return serviceInstances.logicService.post(
      STORAGE_CONFIG_ENDPOINTS.UPDATE_FILE_ADDITIONAL_INFO,
      payload,
    );
  }

  getFilesDownloadUrl(meta: {
    fileId: string;
    projectKey: string;
  }): Promise<IGetFileByFileIDResponse> {
    return serviceInstances.logicService.get(
      `${STORAGE_CONFIG_ENDPOINTS.GET_FILE}?FileId=${meta.fileId}&ProjectKey=${meta.projectKey}`,
    );
  }
}
