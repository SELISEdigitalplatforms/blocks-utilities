import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Check, Pencil } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Card, CardTitle } from "@/components/ui-kits/card/card";
import { IProject } from "@blocks-identifier/models/project.model";
import { useGetEnvRepositories, useUpdateProject } from "@/hooks/use-project";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { formatFullDate } from "@/lib/utils";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { useProjectStore } from "@/store/useProjectStore";
import { EditDomainForm } from "@/components/edit-domain-form/edit-domain-form";

export const ProjectRepoList = ({
  project,
  isLoading,
}: {
  project?: IProject;
  isLoading: boolean;
}) => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const projectKey = useProjectStore().selectedProject?.tenantId || "";
  const itemId = useProjectStore().selectedProject?.itemId || "";
  const [open, setOpen] = useState<boolean>(false);
  const { mutateAsync, isPending } = useUpdateProject({ projectKey: projectKey || "" });
  const {
    data: envRepositoriesResponse,
    isLoading: isLoadingEnvRepos,
    isFetching: isFetchingEnvRepos,
  } = useGetEnvRepositories(project?.tenantId || "");
  const [isSetApplicationDomainModalOpen, setIsSetApplicationDomainModalOpen] =
    useState<boolean>(false);
  const [applicationDomain, setApplicationDomain] = useState<string>(
    project?.applicationDomain || "",
  );
  const [customDomain, setCustomDomain] = useState<string>("");

  const confirmationModalData = {
    dialogTitle: "Set as application domain?",
    dialogSubtitle: "Are you sure you want to set it as the application domain?",
    confirmButton: "Set",
    cancelButton: "Cancel",
  };

  const saveApplicationDomain = async () => {
    try {
      if (!project?.itemId || !projectKey || !applicationDomain || applicationDomain === "") return;
      const res = await mutateAsync({
        name: project.name,
        tenantGroupId: projectKey || "",
      });
      if (res.isSuccess) {
        showSuccessToast({ description: "Application Domain is updated successfully" });
        queryClient.invalidateQueries({
          queryKey: ["identifier", "project", { projectId: itemId }],
        });
        setIsSetApplicationDomainModalOpen(false);
      } else {
        showErrorToast({ errors: res.errors });
      }
    } catch (error) {
      if (error && typeof error === "object" && "errors" in error) {
        showErrorToast({ errors: (error as any).errors });
      }
    }
  };

  if (isLoading || isLoadingEnvRepos || isFetchingEnvRepos) {
    return (
      <div className="mt-6 rounded-lg border bg-card px-2 py-2 shadow-sm md:mt-0">
        <div className="grid-col-1 grid gap-3 px-2 py-4 md:grid-cols-2 md:gap-4 lg:gap-6">
          {Array.from({ length: 6 }).map((_item, index) => (
            <div key={index}>
              <Skeleton className="h-5 w-1/2" />
              <Skeleton className="mt-2 h-5 w-full" />
            </div>
          ))}
        </div>
      </div>
    );
  }

  return (
    <Card className="mt-6 border bg-card px-4 py-4 shadow-sm md:mt-0">
      <div className="mt-2 flex items-center justify-between">
        <CardTitle>Repositories</CardTitle>
        <Dialog open={open} onOpenChange={setOpen}>
          <DialogTrigger>
            <Button
              disabled={
                !envRepositoriesResponse?.data?.length ||
                !project?.customDomain ||
                project?.customDomain === ""
              }
              variant="outline"
            >
              <Pencil className="mr-2 h-3.5 w-3.5" />
              Edit domain
            </Button>
          </DialogTrigger>
          <DialogContent className="max-h-[90vh] overflow-y-auto">
            <DialogHeader>
              <DialogTitle>Configure domain</DialogTitle>
              <DialogDescription>
                Configure your domain to point to your application
              </DialogDescription>
            </DialogHeader>
            <EditDomainForm
              customDomain={project?.customDomain || ""}
              repositories={envRepositoriesResponse?.data ?? []}
              onAfterSubmit={() => setOpen(false)}
            />
          </DialogContent>
        </Dialog>
      </div>
      <div className="mt-4">
        {envRepositoriesResponse &&
        envRepositoriesResponse.data &&
        envRepositoriesResponse.data.length > 0 ? (
          <>
            {/* Mobile & Tablet Card Layout */}
            <div className="lg:hidden">
              {envRepositoriesResponse.data.map((repo, index) => {
                const isDefaultDate = repo.lastDeploymentDate === "0001-01-01T00:00:00";
                return (
                  <div
                    key={index}
                    className="mt-4 space-y-3 rounded-sm border border-border p-4"
                  >
                    <div>
                      <div className="text-xs font-medium text-medium-emphasis">Name</div>
                      <div className="mt-1 break-words text-sm font-medium">{repo.repoName}</div>
                    </div>
                    <div>
                      <div className="text-xs font-medium text-medium-emphasis">Deployment Domain</div>
                      <div className="mt-1 break-words text-sm text-medium-emphasis">
                        {repo.customDeploymentUrl && repo.customDeploymentUrl !== ""
                          ? repo.customDeploymentUrl
                          : repo.defaultDeploymentUrl}
                      </div>
                    </div>
                    <div>
                      <div className="text-xs font-medium text-medium-emphasis">Application Domain</div>
                      <div className="mt-1">
                        {(() => {
                          const hasCustomUrl = !!repo.customDeploymentUrl;
                          const activeDomain = hasCustomUrl
                            ? repo.customDeploymentUrl
                            : repo.defaultDeploymentUrl;
                          const cd = hasCustomUrl ? repo.customDeploymentUrl : "";
                          return activeDomain !== project?.applicationDomain ? (
                            <Button
                              variant="outline"
                              size="xxs"
                              disabled={isPending}
                              onClick={() => {
                                setApplicationDomain(activeDomain);
                                setCustomDomain(cd);
                                setIsSetApplicationDomainModalOpen(true);
                              }}
                            >
                              <span className="px-2">Set</span>
                            </Button>
                          ) : (
                            <div className="flex items-center gap-2">
                              <Check className="h-4 w-4 text-green-500" />
                              <span className="text-sm text-green-600">Active</span>
                            </div>
                          );
                        })()}
                      </div>
                    </div>
                    <div>
                      <div className="text-xs font-medium text-medium-emphasis">Last Deployment Date</div>
                      <div className="mt-1">
                        <div
                          className="cursor-pointer text-sm text-blue-600 hover:text-blue-800 hover:underline"
                          onClick={() => navigate(`/devops/repo/${repo.itemId}`)}
                        >
                          {!repo.lastDeploymentDate || isDefaultDate
                            ? "Not deployed"
                            : formatFullDate(new Date(repo.lastDeploymentDate))}
                        </div>
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
            {/* Desktop Grid Layout */}
            <div className="hidden lg:block">
              <div className="mt-2 grid grid-cols-6 gap-2">
                <div className="col-span-2 text-sm font-medium">Name</div>
                <div className="col-span-2 text-sm font-medium">Deployment Domain</div>
                <div className="flex items-center justify-center text-sm font-medium">Application Domain</div>
                <div className="text-sm font-medium">Last Deployment Date</div>
              </div>
              {envRepositoriesResponse.data.map((repo, index) => {
                const isDefaultDate = repo.lastDeploymentDate === "0001-01-01T00:00:00";
                return (
                  <div key={index} className="mt-4 grid grid-cols-6 gap-2">
                    <div className="col-span-2 overflow-hidden text-ellipsis text-sm font-medium">
                      {repo.repoName}
                    </div>
                    <div className="col-span-2 overflow-hidden text-ellipsis text-sm text-medium-emphasis">
                      {repo.customDeploymentUrl && repo.customDeploymentUrl !== ""
                        ? repo.customDeploymentUrl
                        : repo.defaultDeploymentUrl}
                    </div>
                    <div className="flex items-center justify-center">
                      {(() => {
                        const hasCustomUrl = !!repo.customDeploymentUrl;
                        const activeDomain = hasCustomUrl
                          ? repo.customDeploymentUrl
                          : repo.defaultDeploymentUrl;
                        const cd = hasCustomUrl ? repo.customDeploymentUrl : "";
                        return activeDomain !== project?.applicationDomain ? (
                          <Button
                            variant="outline"
                            size="xxs"
                            disabled={isPending}
                            onClick={() => {
                              setApplicationDomain(activeDomain);
                              setCustomDomain(cd);
                              setIsSetApplicationDomainModalOpen(true);
                            }}
                          >
                            <span className="px-2">Set</span>
                          </Button>
                        ) : (
                          <Check className="h-4 w-4 text-green-500" />
                        );
                      })()}
                    </div>
                    <div className="text-sm text-medium-emphasis">
                      <div
                        className="cursor-pointer text-blue-600 hover:text-blue-800 hover:underline"
                        onClick={() => navigate(`/devops/repo/${repo.itemId}`)}
                      >
                        {!repo.lastDeploymentDate || isDefaultDate
                          ? "Not deployed"
                          : formatFullDate(new Date(repo.lastDeploymentDate))}
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          </>
        ) : (
          <div className="text-medium-emphasis">No repositories found for this project.</div>
        )}
      </div>
      <Dialog
        open={isSetApplicationDomainModalOpen}
        onOpenChange={setIsSetApplicationDomainModalOpen}
      >
        <ConfirmationModal
          onCancel={() => setIsSetApplicationDomainModalOpen(false)}
          onConfirm={saveApplicationDomain}
          data={confirmationModalData}
          buttonState={{ confirm: { disable: isPending } }}
        />
      </Dialog>
    </Card>
  );
};
