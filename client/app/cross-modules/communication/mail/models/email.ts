export interface IEmailServiceData {
  id: string;
  name: string;
  configuration: string;
  subject: string;
  lastModified: Date;
  createdOn: Date;
  createdBy: string;
}

export interface IEmailLogs {
  itemId: string;
  timestamp: string;
  source: string;
  message: string;
  type: "divider" | "message" | "empty";
}

export interface IEmailTemplate {
  itemId: string;
  createdDate?: string;
  lastUpdatedDate?: string;
  createdBy?: string;
  lastUpdatedBy?: string;
  organizationIds?: string[];
  tags?: string[];
  mailConfigurationId?: string;
  templateBody?: string;
  jsonContent?: string;
  imageId?: string;
  imageUrl?: string;
  language?: string;
  name?: string;
  templateSubject?: string;
  generatedBy?: string;
}
export interface OpenStates {
  [key: string]: boolean;
}

export interface LogsEntryProps {
  logs: IEmailLogs[];
}

export enum MailServiceProvider {
  AmazonSes,
  Zoho,
}

export interface IEmailConfig {
  configurationId: string;
  configurationName: string;
  host: string;
  port: number;
  enableSSL: boolean;
  senderName: string;
  senderAddress: string;
  senderUserName: string;
  accountPassword: string;
  itemId: string;
  name: string;
  isDefault: boolean;
  isInbound: boolean;
  provider: MailServiceProvider;
}

export enum MailStatus {
  Sent = "Sent",
  Delivered = "Delivered",
  Bounced = "Bounced",
  Complained = "Complained",
  Rejected = "Rejected",
  Received = "Received",
}

export interface IEmailUsage {
  messageId: string;
  subject: string;
  from: string;
  to: string;
  body: string;
  status: string;
  error: string;
  date: string;
  rawMime: string | null;
  isInbound?: boolean;
}

export interface IEmailUsageResponse {
  totalCount: number;
  mails: IEmailUsage[];
  errors: any;
  isSuccess: boolean;
}

export interface IGetMailBoxMailResponse {
  mail: IEmailUsage;
  errors: any;
  isSuccess: boolean;
}
