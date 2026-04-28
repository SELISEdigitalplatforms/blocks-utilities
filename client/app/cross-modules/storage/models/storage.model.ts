// "Azure"
export type StorageStrategyType = "Amazon" | "Azure" | "SftpStorage" | "S3Compatible";

export interface StorageStrategyOption {
  id: string;
  label: string;
  value: StorageStrategyType;
}

export const STORAGE_STRATEGIES: StorageStrategyOption[] = [
  { id: "aws", label: "AWS", value: "Amazon" },
  { id: "azure", label: "Azure", value: "Azure" },
  { id: "sftp", label: "SFTP", value: "SftpStorage" },
  { id: "s3compatible", label: "AWS S3 Compatible", value: "S3Compatible" },
];
export interface IStorageConfiguration {
  storageStrategy: StorageStrategyType;
  accessKey: string | null;
  cloudStorageRegionEndPoint: string | null;
  connectionString: string | null;
  createdBy: string;
  createdDate: string;
  itemId: string;
  lastUpdatedBy: string;
  lastUpdatedDate: string;
  name: string;
  organizationIds: string[];
  secretKey: string | null;
  tags: string[];
  host: string | null;
  port: string | null;
  userName: string | null;
  password: string | null;
  remoteBasePath: string | null;
}

export interface IStorageConfigurationSavePayload {
  name: string;
  projectKey: string;
  storageStrategy: StorageStrategyType;
  secretKey: string | null;
  accessKey: string | null;
  cloudStorageRegionEndPoint: string | null;
  connectionString: string | null;
  updateRequest: boolean;
  itemId: string | null;
  host: string | null;
  port: string | null;
  userName: string | null;
  password: string | null;
  remoteBasePath: string | null;
}
export interface IStorageConfigurationDeletePayload {
  projectKey: string;
  configurationName: string;
}

export interface IGetPreSignedUrlForUploadPayload {
  itemId?: string;
  name: string;
  configurationName: string;
  projectKey: string;
  metaData: string;
  parentDirectoryId: string;
  tags: string;
  accessModifier: string;
  agentId?: string;
  additionalProperties?: Record<string, unknown>;
  moduleName: number;
}

export interface IGetPreSignedUrlForUploadResponse {
  errors: null | unknown;
  isSuccess: boolean;
  fileId: string;
  uploadUrl: string;
}

export interface IGetFileByFileIDPayload {
  itemId: string;
  projectKey: string;
  configurationName?: string;
}

export interface IGetFileByFileIDResponse {
  url: string;
  accessModifier: number;
  itemId: string;
  tags: string[];
  metaData: Record<string, unknown>;
  name: string;
  parentDirectoryID: string;
  systemName: string;
  type: number;
  typeString: string;
  createDate: string;
  createdBy: string;
  language: string;
  tenantId: string;
  sizeInBytes: number;
  errors: unknown;
  isSuccess: boolean;
}
export interface IUploadImagePayload {
  url: string;
  file: File | Blob;
}

export interface IPublicCertificatePayload {
  TenantId: string;
  file: File;
}

export interface IUploadFileToLocalStorage {
  ItemId: string;
  File: File;
  MetaData: string;
  Name: string;
  ParentDirectoryId: string;
  Tags: string[];
  AccessModifier: string;
  ConfigurationName: string;
  ProjectKey: string;
}

export interface IDeleteResourceBasePayload {
  projectKey: string;
  configurationName?: string;
}

export interface IDeleteFilePayload extends IDeleteResourceBasePayload {
  fileId: string;
}

export interface IDeleteFolderPayload extends IDeleteResourceBasePayload {
  folderId: string;
}
export interface IDeleteResourceResponse {
  errors: unknown;
  isSuccess: boolean;
}

export interface IGetFilesInfoPayload {
  page: number;
  pageSize: number;
  sort: {
    property: string;
    isDescending: boolean;
  };
  filter?: {
    name?: string;
    additionalProperties?: {
      agentId?: string;
      agentStatus?: string;
    };
  };
  projectKey: string;
}

export type IFile = {
  url: string;
  tenantId: string;
  accessModifier: number;
  metaData: {
    additionalProp1: {
      type: string;
      value: string;
    };
    additionalProp2: {
      type: string;
      value: string;
    };
    additionalProp3: {
      type: string;
      value: string;
    };
  };
  additionalProperties: Record<string, unknown>;
  name: string;
  parentDirectoryID: string;
  systemName: string;
  type: number;
  typeString: string;
  currentVersion: number;
  itemId: string;
};

export interface IGetFilesInfoResponse {
  data: IFile[];
  errors: unknown;
  totalCount: number;
  //  itemId: string;
}

export interface IUpdateFileAdditionalInfoPayload {
  itemId: string;
  additionalProperties: Record<string, unknown>;
  projectKey: string;
}

export interface IUpdateFileAdditionalInfoResponse {
  data?: IFile[];

  errors: unknown;
  isSuccess: boolean;
}

export interface IGetDmsFileAndFolderPayload {
  parentId?: string;
  configurationName: string;
  projectKey: string;
  searchKey?: string;
  moduleName?: string;
  skip: number;
  take: number;
}

export enum DmsItemType {
  File = 1,
  Folder = 2,
}

export interface IDmsFileAndFolderInfo {
  parentId: string;
  type: DmsItemType;
  name: string;
  fileStorageId: string;
  extension: string;
  sizeInBytes: string;
  version: number;
  description: string;
  itemId: string;
  lastUpdatedDate: string;
}

export interface IGetDmsFileAndFolderResponse {
  dmsFileAndFolderInfos: IDmsFileAndFolderInfo[];
  totalCount: number;
}

export interface IDmsMetaDataItem {
  type: string;
  value: string;
}

export interface IDmsUploadItem {
  artifactName: string;
  description: string;
  parentId: string;
  tags: string[];
  metaData: Record<string, IDmsMetaDataItem>;
  organizationId: string;
  fileStorageId: string;
  configurationName: string;
}

export interface IUploadDmsFilePayload {
  upload: IDmsUploadItem[];
  projectKey: string;
  name?: string;
}

export interface IUploadDmsFileResult {
  fileStorageId: string;
  success: boolean;
}

export interface IUploadDmsFileResponse {
  result: IUploadDmsFileResult[];
  message: string;
  httpStatusCode: number;
}

export interface IDmsMetaDataValue {
  type: string;
  value: string;
}

export interface ICreateDmsFolderPayload {
  artifactName: string;
  description: string;
  parentId: string;
  tags: string[];
  metaData: {
    additionalProp1?: IDmsMetaDataValue;
    additionalProp2?: IDmsMetaDataValue;
    additionalProp3?: IDmsMetaDataValue;
    [key: string]: IDmsMetaDataValue | undefined;
  };
  organizationId: string;
  fileStorageId: string;
  projectKey: string;
  configurationName: string;
}
