import { useQueryState } from "nuqs";

import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { useContext, useEffect } from "react";
import { LogsViewerContext } from "../logs-viewer/logs-viewer";
// LMTQueryAgentSheet is from @blocks-ai which is not available in IDP standalone
const LMTQueryAgentSheet = ({ questions }: { questions?: unknown[] }) => null;

export const LogsListHeader = () => {
  const { services, changeService, predefinedQueries } = useContext(LogsViewerContext);
  const [tab, setTab] = useQueryState("tab", { defaultValue: services[0].serviceName });
  useEffect(() => {
    if (tab) {
      const service = services.find((item) => item.serviceName === tab);
      if (service) {
        changeService(service);
      }
    }
  }, [changeService, services, tab]);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-lg font-semibold md:text-2xl">Logs</h1>
        </div>
      </div>
      <div className="flex items-center justify-between">
        <Tabs defaultValue={tab} onValueChange={setTab}>
          {services.length && (
            <TabsList>
              {services.map((item) => (
                <TabsTrigger key={item.id} value={item.serviceName} className="w-fit">
                  {item.label}
                </TabsTrigger>
              ))}
            </TabsList>
          )}
        </Tabs>
        <LMTQueryAgentSheet questions={predefinedQueries} />
      </div>
    </div>
  );
};
