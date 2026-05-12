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
    <div className="relative flex flex-1 flex-col overflow-hidden">
      <div className="pointer-events-none absolute -top-20 right-0 h-[600px] w-[600px] rounded-full bg-primary/[0.07] blur-[100px]" />
      <div className="pointer-events-none absolute left-1/3 top-10 h-[350px] w-[350px] rounded-full bg-primary/[0.04] blur-[80px]" />

      <div className="relative flex flex-1 flex-col gap-12 px-6 py-10 sm:px-10 xl:px-[154px]">
        <SelfProject />

        <section className="flex flex-col gap-5">
          <div className="flex items-center gap-3">
            <span className="inline-flex items-center rounded-full bg-primary/10 px-2.5 py-1 text-xs font-semibold tracking-wide text-primary">
              Resources
            </span>
            <div className="h-px flex-1 bg-[hsl(var(--border-default))]" />
          </div>
          <DefaultDoc />
        </section>
      </div>
    </div>
  );
};
