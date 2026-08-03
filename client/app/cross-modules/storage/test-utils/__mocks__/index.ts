import { vi } from "vitest";
import type {
  IStorageConfiguration,
  IStorageConfigurationSavePayload,
  IStorageConfigurationDeletePayload,
  IGetPreSignedUrlForUploadPayload,
  IGetPreSignedUrlForUploadResponse,
  IGetFileByFileIDPayload,
  IGetFileByFileIDResponse,
  IDeleteFilePayload,
  IDeleteResourceResponse,
  IGetFilesInfoPayload,
  IGetFilesInfoResponse,
  IGetDmsFileAndFolderPayload,
  IGetDmsFileAndFolderResponse,
  IUploadDmsFilePayload,
  IUploadDmsFileResponse,
  ICreateDmsFolderPayload,
  DmsItemType,
} from "../../models/storage.model";

// ─── Storage configuration mock data ─────────────────────────────────────────

export const mockStorageConfigList: IStorageConfiguration[] = [
  {
    storageStrategy: "AWS",
    accessKey: "AKIAIOSFODNN7EXAMPLE",
    cloudStorageRegionEndPoint: "us-east-1",
    connectionString: null,
    createdBy: "user-1",
    createdDate: "2026-01-01T00:00:00.000Z",
    itemId: "config-1",
    lastUpdatedBy: "user-1",
    lastUpdatedDate: "2026-01-10T00:00:00.000Z",
    name: "Amazon S3 Config",
    organizationIds: ["org-1"],
    secretKey: "mock-secret-key-for-tests",
    tags: [],
    host: null,
    port: null,
    userName: null,
    password: null,
    remoteBasePath: null,
  },
  {
    storageStrategy: "Azure",
    accessKey: null,
    cloudStorageRegionEndPoint: null,
    connectionString: "DefaultEndpointsProtocol=https;AccountName=example;AccountKey=abc123==",
    createdBy: "user-1",
    createdDate: "2026-01-05T00:00:00.000Z",
    itemId: "config-2",
    lastUpdatedBy: "user-1",
    lastUpdatedDate: "2026-01-10T00:00:00.000Z",
    name: "Azure Blob Config",
    organizationIds: ["org-1"],
    secretKey: null,
    tags: [],
    host: null,
    port: null,
    userName: null,
    password: null,
    remoteBasePath: null,
  },
];

export const mockSuccessResponse = {
  isSuccess: true,
  errors: null,
  itemId: "config-1",
};

export const mockDeleteSuccessResponse: IDeleteResourceResponse = {
  isSuccess: true,
  errors: null,
};

export const mockSaveAmazonConfigPayload: IStorageConfigurationSavePayload = {
  name: "Amazon S3 Config",
  projectKey: "test-project-key-123",
  storageStrategy: "Amazon",
  secretKey: "mock-secret-key-for-tests",
  accessKey: "AKIAIOSFODNN7EXAMPLE",
  cloudStorageRegionEndPoint: "us-east-1",
  connectionString: null,
  updateRequest: false,
  itemId: null,
  host: null,
  port: null,
  userName: null,
  password: null,
  remoteBasePath: null,
};

export const mockSaveAzureConfigPayload: IStorageConfigurationSavePayload = {
  name: "Azure Blob Config",
  projectKey: "test-project-key-123",
  storageStrategy: "Azure",
  secretKey: null,
  accessKey: null,
  cloudStorageRegionEndPoint: null,
  connectionString: "DefaultEndpointsProtocol=https;AccountName=example;AccountKey=abc123==",
  updateRequest: false,
  itemId: null,
  host: null,
  port: null,
  userName: null,
  password: null,
  remoteBasePath: null,
};

export const mockSaveSftpConfigPayload: IStorageConfigurationSavePayload = {
  name: "SFTP Config",
  projectKey: "test-project-key-123",
  storageStrategy: "SftpStorage",
  secretKey: null,
  accessKey: null,
  cloudStorageRegionEndPoint: null,
  connectionString: null,
  updateRequest: false,
  itemId: null,
  host: "sftp.example.com",
  port: "22",
  userName: "sftp-user",
  password: "sftp-password",
  remoteBasePath: "/uploads",
};

export const mockSaveS3CompatibleConfigPayload: IStorageConfigurationSavePayload = {
  name: "S3 Compatible Config",
  projectKey: "test-project-key-123",
  storageStrategy: "S3Compatible",
  secretKey: "secret-key-example",
  accessKey: "access-key-example",
  cloudStorageRegionEndPoint: null,
  connectionString: null,
  updateRequest: false,
  itemId: null,
  host: "s3.compatible.example.com",
  port: null,
  userName: null,
  password: null,
  remoteBasePath: null,
};

export const mockDeleteConfigPayload: IStorageConfigurationDeletePayload = {
  projectKey: "test-project-key-123",
  configurationName: "Amazon S3 Config",
};

// ─── Storage file mock data ───────────────────────────────────────────────────

export const mockGetFileByIdResponse: IGetFileByFileIDResponse = {
  url: "https://storage.example.com/files/file-1.pdf",
  accessModifier: 1,
  itemId: "file-1",
  tags: ["document"],
  metaData: { agentId: "agent-1" },
  name: "file-1.pdf",
  parentDirectoryID: "dir-1",
  systemName: "file-1-system.pdf",
  type: 1,
  typeString: "File",
  createDate: "2026-01-10T00:00:00.000Z",
  createdBy: "user-1",
  language: "en",
  tenantId: "test-tenant-id-123",
  sizeInBytes: 204800,
  errors: null,
  isSuccess: true,
};

export const mockPreSignedUrlResponse: IGetPreSignedUrlForUploadResponse = {
  errors: null,
  isSuccess: true,
  fileId: "file-generated-001",
  uploadUrl: "https://s3.amazonaws.com/bucket/file-generated-001?X-Amz-Signature=abc123",
};

export const mockGetFilesInfoResponse: IGetFilesInfoResponse = {
  data: [
    {
      url: "https://storage.example.com/files/file-1.pdf",
      tenantId: "test-tenant-id-123",
      accessModifier: 1,
      metaData: {
        additionalProp1: { type: "string", value: "value1" },
        additionalProp2: { type: "string", value: "value2" },
        additionalProp3: { type: "string", value: "value3" },
      },
      additionalProperties: {},
      name: "file-1.pdf",
      parentDirectoryID: "dir-1",
      systemName: "file-1-system.pdf",
      type: 1,
      typeString: "File",
      currentVersion: 1,
      itemId: "file-1",
    },
  ],
  errors: null,
  totalCount: 1,
};

export const mockGetDmsFileAndFolderResponse: IGetDmsFileAndFolderResponse = {
  dmsFileAndFolderInfos: [
    {
      parentId: "",
      type: 1 as DmsItemType,
      name: "document.pdf",
      fileStorageId: "storage-001",
      extension: ".pdf",
      sizeInBytes: "204800",
      version: 1,
      description: "A test document",
      itemId: "dms-item-1",
      lastUpdatedDate: "2026-01-10T00:00:00.000Z",
    },
  ],
  totalCount: 1,
};

export const mockUploadDmsFileResponse: IUploadDmsFileResponse = {
  result: [
    {
      fileStorageId: "storage-001",
      success: true,
    },
  ],
  message: "Upload successful",
  httpStatusCode: 200,
};

// ─── Storage payload mocks ────────────────────────────────────────────────────

export const mockGetFilePayload: IGetFileByFileIDPayload = {
  itemId: "file-1",
  projectKey: "test-project-key-123",
  configurationName: "Amazon S3 Config",
};

export const mockDeleteFilePayload: IDeleteFilePayload = {
  fileId: "file-1",
  projectKey: "test-project-key-123",
  configurationName: "Amazon S3 Config",
};

export const mockPreSignedUrlPayload: IGetPreSignedUrlForUploadPayload = {
  name: "upload.pdf",
  configurationName: "Amazon S3 Config",
  projectKey: "test-project-key-123",
  metaData: "{}",
  parentDirectoryId: "dir-1",
  tags: "[]",
  accessModifier: "public",
  moduleName: 1,
};

export const mockGetFilesInfoPayload: IGetFilesInfoPayload = {
  page: 1,
  pageSize: 20,
  sort: {
    property: "name",
    isDescending: false,
  },
  projectKey: "test-project-key-123",
};

export const mockGetDmsPayload: IGetDmsFileAndFolderPayload = {
  configurationName: "Amazon S3 Config",
  projectKey: "test-project-key-123",
  skip: 0,
  take: 20,
};

export const mockUploadDmsFilePayload: IUploadDmsFilePayload = {
  upload: [
    {
      artifactName: "document.pdf",
      description: "A test document",
      parentId: "",
      tags: [],
      metaData: {},
      organizationId: "org-1",
      fileStorageId: "storage-001",
      configurationName: "Amazon S3 Config",
    },
  ],
  projectKey: "test-project-key-123",
  name: "document.pdf",
};

export const mockCreateDmsFolderPayload: ICreateDmsFolderPayload = {
  artifactName: "New Folder",
  description: "A test folder",
  parentId: "",
  tags: [],
  metaData: {},
  organizationId: "org-1",
  fileStorageId: "",
  projectKey: "test-project-key-123",
  configurationName: "Amazon S3 Config",
};

// ─── Service factory ──────────────────────────────────────────────────────────

export const mockStorageServiceFactory = () => ({
  storageService: {
    configuration: {
      gets: vi.fn(),
      save: vi.fn(),
      delete: vi.fn(),
    },
    file: {
      getFileByFileId: vi.fn(),
      deleteFileByFileId: vi.fn(),
      getPreSignedUrlForUpload: vi.fn(),
      getFilesInfoUrlForUpload: vi.fn(),
      updateFileAdditionalInfo: vi.fn(),
      getFilesDownloadUrl: vi.fn(),
    },
    uploadFile: vi.fn(),
    uploadFileToLocalStorage: vi.fn(),
    uploadPublicCertificateFile: vi.fn(),
    getFilesAndFolders: vi.fn(),
    uploadDmsFile: vi.fn(),
    createDmsFolder: vi.fn(),
  },
});
