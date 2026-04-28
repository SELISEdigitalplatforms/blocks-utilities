import { http } from "@/lib/http-client";
import { StorageConfiguration } from "./storage-configuration.service";
import { StorageFile } from "./storage-file.service";
import { STORAGE_FILE_ENDPOINTS } from "../constants/endpoint.constant";
import {
  ICreateDmsFolderPayload,
  IGetDmsFileAndFolderPayload,
  IGetDmsFileAndFolderResponse,
  IPublicCertificatePayload,
  IUploadDmsFilePayload,
  IUploadDmsFileResponse,
  IUploadFileToLocalStorage,
  IUploadImagePayload,
} from "../models/storage.model";

export class StorageService {
  constructor(
    public configuration: StorageConfiguration,
    public file: StorageFile,
  ) { }

  uploadFile(payload: IUploadImagePayload): Promise<{}> {
    return http.put(
      payload.url,
      payload.file,
      {
        "Content-Type": payload.file.type,
        "x-ms-blob-type": "Blockblob",
      },
      { skipBlocksKey: true, absoluteUrl: true, withCredentials: false },
    );
  }

  uploadFileToLocalStorage(payload: IUploadFileToLocalStorage): Promise<{}> {
    const formData = (Object.keys(payload) as (keyof IUploadFileToLocalStorage)[]).reduce(
      (acc, key) => {
        const value = payload[key];
        acc.append(key, value instanceof Blob ? value : value.toString());
        return acc;
      },
      new FormData(),
    );
    return http.post(STORAGE_FILE_ENDPOINTS.UPLOAD_TO_LOCAL_STORAGE, formData);
  }

  uploadPublicCertificateFile(
    payload: IPublicCertificatePayload,
  ): Promise<{ downloadUrl: string }> {
    const formData = new FormData();

    formData.append(
      "Certificate",
      payload.file,
      (payload.file as File)?.name ?? "public-certificate.pfx",
    );
    return http.post(
      `${STORAGE_FILE_ENDPOINTS.UPLOAD_PUBLIC_CERTIFICATE}?TenantId=${payload.TenantId}&IsThirdParty=true`,
      formData,
      { Accept: "*/*" },
    );
  }

  getFilesAndFolders(payload: IGetDmsFileAndFolderPayload): Promise<IGetDmsFileAndFolderResponse> {
    return http.post(STORAGE_FILE_ENDPOINTS.GET_DMS_FILE_AND_FOLDER, payload);
  }

  uploadDmsFile(payload: IUploadDmsFilePayload): Promise<IUploadDmsFileResponse> {
    return http.post(STORAGE_FILE_ENDPOINTS.UPLOAD_DMS_FILE, payload);
  }

  createDmsFolder(payload: ICreateDmsFolderPayload): Promise<IUploadDmsFileResponse> {
    return http.post(STORAGE_FILE_ENDPOINTS.CREATE_FOLDER, payload);
  }
}

export const storageService = new StorageService(new StorageConfiguration(), new StorageFile());
