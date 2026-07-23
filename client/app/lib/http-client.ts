import { getRuntimeEnv } from "@/lib/runtime-env";
import { HttpClient } from "@seliseblocks/blocks-kit/lib";

export const serviceInstances = {
  utitlitiesService: new HttpClient({
    baseURL: getRuntimeEnv("BLOCKS_UTILITIES_BASE_URL") || "",
    blocksKey: getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
  }),
  logicService: new HttpClient({
    baseURL: getRuntimeEnv("BLOCKS_LOGIC_BASE_URL") || "",
    blocksKey: getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
  }),
  idpService: new HttpClient({
    baseURL: getRuntimeEnv("BLOCKS_IAM_BASE_URL") || "",
    blocksKey: getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
  }),
};

export { HttpClient };
