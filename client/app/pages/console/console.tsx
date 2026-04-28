import { useEffect } from "react";
import { useProjectStore } from "@/store/useProjectStore";
import { DefaultDoc } from "./default-doc";
import { SelfProject } from "./self-project";

export const Console = () => {
  const { resetSelectedProject } = useProjectStore();
  useEffect(() => {
    resetSelectedProject();
  }, [resetSelectedProject]);

  return (
    <main className="flex flex-1 flex-col gap-4 p-4 sm:mx-10 md:gap-6">
      <SelfProject />
      <div className="mt-20">
        <DefaultDoc />
      </div>
    </main>
  );
};
