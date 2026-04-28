

import { Card, CardContent } from "@/components/ui-kits/card/card";
import { AddEditProviderModal } from "./add-edit-provider-modal";

export const EmptyConfiguration = () => {
  return (
    <Card>
      <CardContent className="flex flex-col items-center justify-between gap-3">
        <p>No provider configured.</p>
        <AddEditProviderModal />
      </CardContent>
    </Card>
  );
};
