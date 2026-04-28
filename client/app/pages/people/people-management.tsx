import { useGetPeople } from "@/hooks/use-people";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { Badge } from "@/components/ui-kits/badge/badge";

const PeopleManagementLoading = () => (
  <main className="flex flex-col p-6">
    <div className="flex items-center justify-between">
      <Skeleton className="h-8 w-24" />
      <Skeleton className="h-10 w-32" />
    </div>
    <div className="mb-5 mt-4 flex w-full flex-col">
      <Card>
        <CardHeader>
          <Skeleton className="h-10 w-full" />
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <div className="space-y-4">
            {Array.from({ length: 5 }).map((_, i) => (
              <Skeleton key={i} className="h-12 w-full" />
            ))}
          </div>
        </CardContent>
      </Card>
    </div>
  </main>
);

export const PeopleManagement = () => {
  const { isLoading, data } = useGetPeople({
    page: 0,
    pageSize: 100,
    filter: "",
  });

  if (isLoading) return <PeopleManagementLoading />;

  const peoples = data?.peoples || [];
  const isViewerOwner = data?.isOwner ?? false;

  return (
    <main className="flex flex-col p-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <h4 className="text-lg font-semibold md:text-xl">People</h4>
        </div>
      </div>

      <div className="mb-5 mt-4 flex w-full flex-col">
        <Card>
          <CardContent className="pt-6">
            {peoples.length === 0 ? (
              <div className="py-8 text-center text-sm text-muted-foreground">
                No people found in this project.
              </div>
            ) : (
              <div className="space-y-3">
                {peoples.map((person, index) => (
                  <div
                    key={index}
                    className="flex items-center justify-between rounded-md border p-3"
                  >
                    <div className="flex items-center gap-3">
                      <div className="flex h-9 w-9 items-center justify-center rounded-full bg-[var(--avatar-surface-default)] text-sm font-medium text-[var(--avatar-text-high-emphasis)]">
                        {person.peopleDetails?.firstName?.[0] || person.peopleDetails?.email?.[0] || "?"}
                      </div>
                      <div>
                        <div className="text-sm font-medium">
                          {person.peopleDetails?.firstName
                            ? `${person.peopleDetails.firstName} ${person.peopleDetails.lastName || ""}`
                            : person.peopleDetails?.email}
                        </div>
                        <div className="text-xs text-muted-foreground">
                          {person.peopleDetails?.email}
                        </div>
                      </div>
                    </div>
                    <div className="flex items-center gap-2">
                      {person.sharedEnviroments?.map((env) => (
                        <Badge key={env.tenantId} variant="secondary" className="text-xs">
                          {env.enviroment}
                        </Badge>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </main>
  );
};
