import { Card, CardContent } from "@/components/ui-kits/card/card";
import { useGetPats } from "@/idp/iam/hooks/use-activity";
import { UserPATList } from "./user-pats-list";
import { IPATResponse } from "@/idp/iam/models/user";
import { Button } from "@/components/ui-kits/button/button";
import { Sailboat } from "lucide-react";
import { useState } from "react";
import { GenerateTokenModal } from "./generate-pat-modal";
import { useT } from "@seliseblocks/genesis-os";

export const UserPats = ({ id }: { id: string }) => {
  const { t } = useT("utilities.idp");
  const [isModalOpen, setIsModalOpen] = useState(false);

  const { isLoading, isFetching, data } = useGetPats();

  return (
    <div className="flex w-full flex-col">
      <Card>
        <CardContent>
          {!data?.length && !isLoading && !isFetching ? (
            <>
              <div className="mx-auto pb-8">
                <div className="mt-2 space-y-2">
                  <div className="flex h-auto flex-col items-center justify-center gap-6 self-stretch rounded-sm bg-background px-6 py-12">
                    <div className="space-y-4 text-center">
                      <div className="mx-auto flex h-16 w-16 items-center justify-center rounded-full bg-gray-100">
                        <Sailboat className="h-8 w-8 text-low-emphasis" />
                      </div>
                      <div className="space-y-2">
                        <h3 className="text-lg font-semibold text-high-emphasis">
                          {t("USER_PATS.NO_PAT_PERSONAL_ACCESS_TOKEN")}{" "}
                        </h3>
                        <p className="max-w-md text-sm text-low-emphasis">
                          {t("USER_PATS.NO_PAT_AVAILABLE_CREATE_A")}
                        </p>
                      </div>
                      <Button onClick={() => setIsModalOpen(true)} size="sm">
                        {t("USER_PATS.GENERATE_PAT")}
                      </Button>
                    </div>
                  </div>
                </div>
              </div>
              <GenerateTokenModal
                isOpen={isModalOpen}
                onClose={() => setIsModalOpen(false)}
                id={id}
              />
            </>
          ) : (
            <UserPATList
              isLoading={isLoading || isFetching}
              data={(data ? (data as unknown as IPATResponse[]) : []) || []}
              id={id}
            />
          )}
        </CardContent>
      </Card>
    </div>
  );
};
