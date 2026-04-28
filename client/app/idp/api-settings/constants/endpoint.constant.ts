import { API_BASES } from "@/constants/endpoint.constant";

const API_ENDPOINT_CONFIG_SUBPATH = "/ApiEndpointConfig";

export const API_SETTINGS_ENDPOINTS = {
  GET_LIST: `${API_BASES.CLOUD_CONFIGURATION}${API_ENDPOINT_CONFIG_SUBPATH}/GetList`,
  UPDATE: `${API_BASES.CLOUD_CONFIGURATION}${API_ENDPOINT_CONFIG_SUBPATH}/Update`,
  BULK_UPDATE: `${API_BASES.CLOUD_CONFIGURATION}${API_ENDPOINT_CONFIG_SUBPATH}/BulkUpdate`,
  REMOVE: `${API_BASES.CLOUD_CONFIGURATION}${API_ENDPOINT_CONFIG_SUBPATH}/Remove`,
} as const;

/** Client-side metadata for known service groups. */
export const SERVICE_META: Record<string, { description: string; icon: string }> = {
  Authentication: {
    description: "Authentication and user identity provisioning endpoints.",
    icon: "Lock",
  },
  Captcha: {
    description: "Captcha verification and challenge endpoints.",
    icon: "Shield",
  },
  Iam: {
    description: "Identity and access management endpoints.",
    icon: "Users",
  },
  Mfa: {
    description: "Multi-factor authentication endpoints.",
    icon: "Smartphone",
  },
  Storage: {
    description: "S3-compatible object storage and bucket management.",
    icon: "HardDrive",
  },
  Communication: {
    description: "Email, SMS, and notification delivery endpoints.",
    icon: "Mail",
  },
};

export const DEFAULT_SERVICE_META = {
  description: "Service endpoints.",
  icon: "Settings",
};
