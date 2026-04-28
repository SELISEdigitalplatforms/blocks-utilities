

import { ConfigureCaptchaList } from "./configure-captcha-list";
import { useProjectStore } from "@/store/useProjectStore";
import { useGetCaptchaConfigs } from "../../hooks/use-captcha-config";

export const ConfigureCaptcha = () => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { isLoading, isFetching, data } = useGetCaptchaConfigs({ projectKey: tenantId });

  return (
    <div className="flex w-full flex-col">
      <ConfigureCaptchaList
        isLoading={isLoading || isFetching}
        configurations={data?.configurations || []}
      />
    </div>
  );
};
