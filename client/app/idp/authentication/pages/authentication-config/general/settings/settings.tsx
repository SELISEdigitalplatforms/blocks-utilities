
import { ViewAuthConfigure } from "./view-auth-configure";
import { EditGeneralSettings } from "./edit-settings";
import { useIsFetching } from "@tanstack/react-query";
import { useProjectStore } from "@/store/useProjectStore";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";

export function GeneralSettings() {
  const { itemId } = useProjectStore().selectedProject || { itemId: "" };
  const isFetching = useIsFetching({
    queryKey: [
      "identifier",
      "project-auth-config",
      {
        projectId: itemId,
      },
    ],
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center justify-between">
          Settings {!isFetching && <EditGeneralSettings />}
        </CardTitle>
      </CardHeader>
      <CardContent>
        <ViewAuthConfigure />
      </CardContent>
    </Card>
  );
}
