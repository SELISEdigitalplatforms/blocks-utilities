import { API_BASES } from "@/constants/endpoint.constant";
import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_IDP_MODULE = "utilities.idp";

const API_ENDPOINT_CONFIG_SUBPATH = "/ApiEndpointConfig";

export const API_SETTINGS_ENDPOINTS = {
  GET_LIST: `${API_BASES.CLOUD_CONFIGURATION}${API_ENDPOINT_CONFIG_SUBPATH}/GetList`,
  UPDATE: `${API_BASES.CLOUD_CONFIGURATION}${API_ENDPOINT_CONFIG_SUBPATH}/Update`,
  BULK_UPDATE: `${API_BASES.CLOUD_CONFIGURATION}${API_ENDPOINT_CONFIG_SUBPATH}/BulkUpdate`,
  REMOVE: `${API_BASES.CLOUD_CONFIGURATION}${API_ENDPOINT_CONFIG_SUBPATH}/Remove`,
} as const;

/** Client-side metadata for known service groups. */
export const getServiceMeta = (): Record<string, { description: string; icon: string }> => ({
  Authentication: {
    description: tStatic(UTILITIES_IDP_MODULE, "ENDPOINT_CONSTANT.AUTHENTICATION_AND_USER_IDENTITY"),
    icon: "Lock",
  },
  Captcha: {
    description: tStatic(UTILITIES_IDP_MODULE, "ENDPOINT_CONSTANT.CAPTCHA_VERIFICATION_AND_CHALLENGE"),
    icon: "Shield",
  },
  Iam: {
    description: tStatic(UTILITIES_IDP_MODULE, "ENDPOINT_CONSTANT.IDENTITY_AND_ACCESS_MANAGEMENT"),
    icon: "Users",
  },
  Mfa: {
    description: tStatic(UTILITIES_IDP_MODULE, "ENDPOINT_CONSTANT.MULTI_FACTOR_AUTHENTICATION"),
    icon: "Smartphone",
  },
  Storage: {
    description: tStatic(UTILITIES_IDP_MODULE, "ENDPOINT_CONSTANT.S3_COMPATIBLE_OBJECT_STORAGE_AND"),
    icon: "HardDrive",
  },
  Communication: {
    description: tStatic(UTILITIES_IDP_MODULE, "ENDPOINT_CONSTANT.MAGIC_URL_DELIVERY_AND_WORKFLOW"),
    icon: "Mail",
  },
});

export const getDefaultServiceMeta = () => ({
  description: tStatic(UTILITIES_IDP_MODULE, "ENDPOINT_CONSTANT.SERVICE_ENDPOINTS"),
  icon: "Settings",
});
