import { vi } from "vitest";
import type {
  IBlocksLanguageKey,
  ILanguageModule,
  ILanguageConfig,
  IImportFile,
  IKeyUilmExport,
  IGetTimelineResponse,
  IGetExportHistory,
  IRollbackResponse,
  IModuleGets,
} from "../../models/language";

// ─── Language key mock data ───────────────────────────────────────────────────

export const mockBlocksLanguageKey: IBlocksLanguageKey = {
  itemId: "key-1",
  keyName: "common.save",
  moduleId: "module-1",
  routes: ["/dashboard"],
  resources: [
    { value: "Save", culture: "en" },
    { value: "Speichern", culture: "de" },
  ],
  isPartiallyTranslated: false,
  lastUpdateDate: "2026-01-10T00:00:00.000Z",
  createDate: "2026-01-01T00:00:00.000Z",
  context: "Button label for saving changes",
};

export const mockLanguageKeysResponse = {
  totalCount: 2,
  keys: [
    mockBlocksLanguageKey,
    {
      itemId: "key-2",
      keyName: "common.cancel",
      moduleId: "module-1",
      routes: [],
      resources: [
        { value: "Cancel", culture: "en" },
        { value: "Abbrechen", culture: "de" },
      ],
      isPartiallyTranslated: true,
      lastUpdateDate: "2026-01-10T00:00:00.000Z",
      createDate: "2026-01-01T00:00:00.000Z",
      context: "Button label for cancelling",
    },
  ],
};

// ─── Language module mock data ────────────────────────────────────────────────

export const mockLanguageModuleList: ILanguageModule[] = [
  { moduleName: "Common", itemId: "module-1" },
  { moduleName: "Dashboard", itemId: "module-2" },
];

export const mockModuleGetsList: IModuleGets[] = [
  {
    moduleName: "Common",
    name: "Common Module",
    itemId: "module-1",
    createDate: "2026-01-01T00:00:00.000Z",
    lastUpdateDate: "2026-01-10T00:00:00.000Z",
    createdBy: "user-1",
    lastUpdatedBy: "user-1",
    tenantId: "test-tenant-id-123",
  },
];

// ─── Language config mock data ────────────────────────────────────────────────

export const mockLanguageConfigList: ILanguageConfig[] = [
  { itemId: "lang-1", languageName: "English", languageCode: "en", isDefault: true },
  { itemId: "lang-2", languageName: "German", languageCode: "de", isDefault: false },
];

// ─── Generic response mocks ───────────────────────────────────────────────────

export const mockSuccessResponse = {
  isSuccess: true,
  errors: null,
};

export const mockDeleteSuccessResponse = {
  isSuccess: true,
  errors: null,
};

export const mockRollbackResponse: IRollbackResponse = {
  isSuccess: true,
  errors: null,
};

// ─── Timeline mock data ───────────────────────────────────────────────────────

export const mockGetTimelineResponse: IGetTimelineResponse = {
  totalCount: 1,
  timelines: [
    {
      itemId: "timeline-1",
      logFrom: "UI",
      userName: "Test User",
      createDate: "2026-01-10T00:00:00.000Z",
      userId: "user-1",
      previousData: [],
      currentData: [mockBlocksLanguageKey],
    },
  ],
};

// ─── Export history mock data ─────────────────────────────────────────────────

export const mockGetExportHistory: IGetExportHistory = {
  totalCount: 1,
  uilmExportedFiles: [
    {
      fileId: "export-file-1",
      fileName: "export-2026-01-10.json",
      createDate: "2026-01-10T00:00:00.000Z",
      createdBy: "user-1",
    },
  ],
};

// ─── Payload mocks ────────────────────────────────────────────────────────────

export const mockSaveLanguageKeyPayload = {
  itemId: "",
  keyName: "common.new_key",
  moduleId: "module-1",
  resources: [
    { value: "New Key", culture: "en" },
    { value: "Neuer Schlüssel", culture: "de" },
  ],
  routes: [],
  isPartiallyTranslated: false,
  projectKey: "test-project-key-123",
  isNewKey: true,
  context: "",
};

export const mockSaveLanguageModulePayload = {
  moduleName: "New Module",
  projectKey: "test-project-key-123",
};

export const mockSaveLanguagePayload = {
  languageName: "French",
  languageCode: "fr",
  projectKey: "test-project-key-123",
};

export const mockDeleteLanguageKeyPayload = {
  itemId: "key-1",
  projectKey: "test-project-key-123",
};

export const mockDeleteLanguagePayload = {
  languageName: "German",
  projectKey: "test-project-key-123",
};

export const mockSetDefaultPayload = {
  languageName: "English",
  projectKey: "test-project-key-123",
};

export const mockTranslateAllPayload = {
  projectKey: "test-project-key-123",
  messageCoRelationId: "corr-id-001",
  defaultLanguage: "en",
  moduleId: "module-1",
};

export const mockTranslateKeyPayload = {
  keyId: "key-1",
  projectKey: "test-project-key-123",
  defaultLanguage: "en",
  messageCoRelationId: "corr-id-002",
};

export const mockImportFile: IImportFile = {
  messageCoRelationId: "corr-id-003",
  fileId: "import-file-1",
  projectKey: "test-project-key-123",
};

export const mockKeyUilmExport: IKeyUilmExport = {
  outputType: 1,
  messageCoRelationId: "corr-id-004",
  appIds: ["app-1"],
  languages: ["en", "de"],
  referenceFileId: "",
  callerTenantId: "test-tenant-id-123",
  projectKey: "test-project-key-123",
};

export const mockGenerateUilmFilePayload = {
  guid: "guid-001",
  projectKey: "test-project-key-123",
};

export const mockTranslationSuggestionPayload = {
  sourceText: "Save",
  destinationLanguage: "de",
  currentLanguage: "en",
  temperature: 0.7,
  elementDetailContext: "Button label",
};

export const mockTranslationSuggestionResponse = {
  content: "Speichern",
  errors: null,
  isSuccess: true,
};

export const mockRevertKeyTimelinePayload = {
  itemId: "timeline-1",
  projectKey: "test-project-key-123",
};

// ─── Service factory ──────────────────────────────────────────────────────────

export const mockLanguageServiceFactory = () => ({
  languageManagerService: {
    fetchBlocksLanguageKey: vi.fn(),
    fetchBlocksLanguageKeyById: vi.fn(),
    fetchBlocksLanguageModules: vi.fn(),
    fetchBlocksLanguages: vi.fn(),
    saveBlocksLanguageKey: vi.fn(),
    saveLanguageModule: vi.fn(),
    getLanguageModule: vi.fn(),
    saveLanguage: vi.fn(),
    deleteLanguageKey: vi.fn(),
    deleteLanguage: vi.fn(),
    setDefault: vi.fn(),
    generateUilmFile: vi.fn(),
    getTranslationSuggestion: vi.fn(),
    translateAll: vi.fn(),
    translateKey: vi.fn(),
    importLanguageFile: vi.fn(),
    saveLanguageKeyUilmExport: vi.fn(),
    getKeysTimeline: vi.fn(),
    getExportHistory: vi.fn(),
    revertKeyTimeline: vi.fn(),
    getLocalizationTimeline: vi.fn(),
    getTimelineByOperationId: vi.fn(),
  },
});
