import { useProjectStore } from "@/store/useProjectStore";
import { useGetProjects, useGetMigrationStatus } from "@/hooks/use-project";
import { useGetPeople } from "@/hooks/use-people";
import { EnvironmentCard } from "@/components/environment-card/environment-card";
import { AddEnvironmentModal } from "@/components/environment-card/add-environment-modal";
import { Plus, ArrowRightLeft, CircleHelp } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { useState, useCallback } from "react";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { ProjectCardLoading } from "@/components/project-card/loading";
import { useNavigate } from "react-router-dom";
import { useNotificationListener } from "@/cross-modules/communication/hooks/use-notification-listener";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";

const ProjectGroupLoading = () => (
  <main className="flex flex-1 flex-col gap-4 p-4 sm:mx-10 md:gap-6">
    <div className="mt-4">
      <div className="mb-8 flex flex-row items-center justify-between">
        <Skeleton className="h-8 w-40" />
        <div className="flex gap-4">
          <Skeleton className="h-10 w-32" />
          <Skeleton className="h-10 w-40" />
        </div>
      </div>
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
        {Array(8)
          .fill(1)
          .map((_, index) => (
            <ProjectCardLoading key={index} />
          ))}
      </div>
    </div>
  </main>
);

export const EnvironmentsPage = () => {
  const groupId = useProjectStore().selectedTenantGroup;
  const { data: environmentList, isLoading, isFetching } = useGetProjects(groupId ?? "");
  const { data: peopleData } = useGetPeople({ page: 0, pageSize: 1, filter: "" });
  const isViewerOwner = peopleData?.isOwner ?? false;
  const [addEnvModalOpen, setAddEnvModalOpen] = useState(false);
  const navigate = useNavigate();

  const { data: migrationStatus, refetch: refetchMigrationStatus } = useGetMigrationStatus(
    groupId as string,
  );

  const handleMigrationNotification = useCallback(
    (_: unknown) => {
      void refetchMigrationStatus();
    },
    [refetchMigrationStatus],
  );

  useNotificationListener("EnvironmentDataMigration", handleMigrationNotification);

  const handleAddEnvModalClose = () => {
    setAddEnvModalOpen(false);
  };

  if (isLoading || isFetching || !environmentList || !environmentList[0]?.projects[0]) {
    return <ProjectGroupLoading />;
  }

  const canAddEnvironment =
    environmentList && environmentList[0]?.projects?.length < 8 && isViewerOwner;

  return (
    <main className="flex flex-1 flex-col gap-4 p-6 md:gap-6">
      <div>
        <div className="mb-6 flex flex-row justify-between">
          <h4 className="text-lg font-semibold md:text-xl">Environments</h4>
          <div className="flex gap-2 sm:gap-4">
            <Button
              variant="outline"
              size="sm"
              onClick={() => navigate("/data-migration")}
              className="h-10 whitespace-nowrap text-sm"
            >
              <ArrowRightLeft className="mr-2 h-4 w-4" />
              <span className="hidden sm:inline">Start Migration</span>
            </Button>
            {canAddEnvironment && (
              <Button
                variant="default"
                size="sm"
                onClick={() => setAddEnvModalOpen(true)}
                className="h-10 whitespace-nowrap text-sm"
              >
                <Plus className="mr-2 h-4 w-4" />
                <span className="hidden sm:inline">New Environment</span>
              </Button>
            )}
          </div>
        </div>

        {environmentList[0]?.isShared && (
          <div className="mb-4 mt-6 border-b-2 border-border pb-2">
            <h5 className="text-sm font-medium text-muted-foreground">Shared with you</h5>
          </div>
        )}
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {environmentList[0]?.projects?.map((project) => (
            <EnvironmentCard
              key={`shared-${project.itemId}`}
              project={project}
              isMigrationOngoing={
                Array.isArray(migrationStatus) &&
                migrationStatus.some(
                  (data) => data.targetedProjectKey === project.tenantId,
                )
              }
            />
          ))}
        </div>

        {environmentList[0]?.isShared && environmentList[0]?.nonSharedProject?.length > 0 && (
          <>
            <div className="mb-4 mt-8 border-b-2 border-border pb-2">
              <h5 className="text-sm font-medium text-muted-foreground">Others</h5>
            </div>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
              {environmentList[0]?.nonSharedProject?.map((project) => (
                <div key={`others-${project.itemId}`} className="pointer-events-none grayscale">
                  <EnvironmentCard
                    key={`others-${project.itemId}`}
                    project={project}
                    isMigrationOngoing={
                      Array.isArray(migrationStatus) &&
                      migrationStatus.some(
                        (data) => data.targetedProjectKey === project.tenantId,
                      )
                    }
                    className="bg-muted"
                  />
                </div>
              ))}
            </div>
          </>
        )}
      </div>
      <Dialog open={addEnvModalOpen} onOpenChange={setAddEnvModalOpen}>
        <DialogContent className="max-h-[90vh] w-[calc(100%-2rem)] overflow-y-auto rounded-lg border p-6 shadow-lg md:max-h-[85vh] md:w-[500px]">
          <DialogHeader className="mb-4">
            <DialogTitle className="text-lg md:text-xl">Add Environment</DialogTitle>
            <DialogDescription className="flex flex-col gap-2 text-sm md:flex-row md:items-start md:gap-2">
              <span className="flex flex-row items-start gap-2">
                <span>Please add the environments you want to configure.</span>
                <Tooltip>
                  <TooltipTrigger type="button" asChild>
                    <CircleHelp className="mt-0.5 h-4 w-4 flex-shrink-0" />
                  </TooltipTrigger>
                  <TooltipContent className="max-w-xs text-xs font-normal md:max-w-96 md:text-sm">
                    You must have the corresponding branch in your repository.
                  </TooltipContent>
                </Tooltip>
              </span>
            </DialogDescription>
          </DialogHeader>
          <div className="max-h-[calc(90vh-160px)] overflow-y-auto md:max-h-[calc(85vh-160px)]">
            <AddEnvironmentModal
              tenantGroupId={groupId ?? undefined}
              projectName={environmentList && environmentList[0]?.projects[0]?.name}
              preSelectedEnvironments={environmentList
                .map((env) => env.projects.map((p) => p.environment))
                .flat()}
              onClose={handleAddEnvModalClose}
            />
          </div>
        </DialogContent>
      </Dialog>
    </main>
  );
};
