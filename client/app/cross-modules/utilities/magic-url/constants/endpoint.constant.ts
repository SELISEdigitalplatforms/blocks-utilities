import { API_BASES } from "@/constants/endpoint.constant";

const MAGIC_LINK_SUBPATH = "/MagicLink";

export const MAGIC_URL_ENDPOINTS = {
  GET_LINK: `${API_BASES.UTILITIES}${MAGIC_LINK_SUBPATH}/GetLink`,
  GET_LINKS: `${API_BASES.UTILITIES}${MAGIC_LINK_SUBPATH}/GetLinks`,
  CREATE_LINK: `${API_BASES.UTILITIES}${MAGIC_LINK_SUBPATH}/CreateLink`,
  SAVE_CONFIG: `${API_BASES.UTILITIES}${MAGIC_LINK_SUBPATH}/SaveConfig`,
  GET_CONFIG: `${API_BASES.UTILITIES}${MAGIC_LINK_SUBPATH}/GetConfig`,
  REMOVE_LINKS: `${API_BASES.UTILITIES}${MAGIC_LINK_SUBPATH}/RemoveLinks`,
} as const;

export const SHORT_URL_BASES: Record<string, string> = {
  dev: "https://dev-short.seliseblocks.com/",
  stg: "https://stg-short.seliseblocks.com/",
  prod: "https://short.seliseblocks.com/",
};
