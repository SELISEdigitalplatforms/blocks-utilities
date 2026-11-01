import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Plus } from "lucide-react";
import { Form } from "@/components/ui-kits/form/form";
import { Button } from "@/components/ui-kits/button/button";
import { useCreateProjectFormState } from "../../utils";
import ProviderButtons from "@/cross-modules/devops/components/deployment-steps/render-repos/render-provider";
import { useStepper } from "@/components/stepper/stepper-provider";
import {
  CreateProjectResourcesFormDefaultValue,
  CreateProjectResourcesFormSchema,
  Asset,
} from "./utils";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import {
  useGetRepositoryUser,
  useValidateAuthorization,
} from "@/cross-modules/devops/hooks/github-info";
import { IRepository, iconMap } from "@/cross-modules/devops/models/github-info";
import { RepositorySelectionModal } from "@/components/repository-selection-modal/repository-selection-modal";
import { useT } from "@seliseblocks/genesis-os";

export const CreateProjectResourcesForm = () => {
  const { t } = useT("utilities.shared");
  const { nextStep } = useStepper();
  const { formData, setFormData } = useCreateProjectFormState();
  const [repositoryModalOpen, setRepositoryModalOpen] = useState(false);
  const [selectRepositoryModalOpen, setSelectRepositoryModalOpen] = useState(false);

  const form = useForm<{ assets: Asset[] }>({
    values: formData[1],
    resolver: zodResolver(CreateProjectResourcesFormSchema()),
  });

  const [selectedRepositories, setSelectedRepositories] = useState<IRepository[]>(
    (formData[1]?.assets ?? []).map((asset) => ({
      id: asset.id ?? 0,
      name: asset.name,
      full_name: asset.full_name ?? asset.name,
      html_url: asset.html_url,
    })),
  );
  const { data: _isAuthenticated, refetch: refetchAuthorization } = useValidateAuthorization();
  const { data: authAccountData } = useGetRepositoryUser(!!_isAuthenticated?.isSuccess);

  const onSubmitHandler = (values: typeof CreateProjectResourcesFormDefaultValue) => {
    setFormData(1, values);
    nextStep();
  };

  const { isValid } = form.formState;

  const handleAddRepositoryClick = async () => {
    try {
      const authResult = await refetchAuthorization();
      if (authResult.data?.isSuccess) {
        setSelectRepositoryModalOpen(true);
      } else {
        setRepositoryModalOpen(true);
      }
    } catch (error) {
      console.error("Authorization check failed:", error);
      setRepositoryModalOpen(true);
    }
  };

  const handleProviderClose = (verifyAuth?: boolean) => {
    setRepositoryModalOpen(false);
    setSelectRepositoryModalOpen(verifyAuth ? true : false);
  };

  const handleSelectRepository = (repo: IRepository) => {
    if (!selectedRepositories.some((r) => r.id === repo.id)) {
      const updatedRepos = [...selectedRepositories, repo];
      setSelectedRepositories(updatedRepos);
      const assets = updatedRepos.map((r) => ({
        id: r.id,
        name: r.name,
        html_url: r.html_url,
        full_name: r.full_name,
      }));
      form.setValue("assets", assets);
    }
    setSelectRepositoryModalOpen(false);
  };

  const githubIconSrc = iconMap["github"] || "/assets/github-icon.svg";

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(onSubmitHandler)} className="w-full">
        <div className="mt-4 flex flex-col gap-6 text-left">
          <div className="space-y-1">
            <h3 className="text-2xl font-semibold tracking-tight">{t("CREATE_PROJECT_RESOURCES_FORM.ADD_RESOURCE")}</h3>
            <p className="text-base font-normal text-gray-500">
              {t("CREATE_PROJECT_RESOURCES_FORM.ADD_REPOSITORIES_TO_LINK_WITH")}
            </p>
          </div>

          <div className="rounded-md border border-gray-200 bg-card p-6 shadow-sm">
            <h4 className="mb-2 text-lg font-medium">{t("CREATE_PROJECT_RESOURCES_FORM.CONNECT_AND_SELECT_REPOSITORIES")}</h4>
            <p className="mb-6 text-sm text-gray-500">
              {t("CREATE_PROJECT_RESOURCES_FORM.WE_LL_AUTOMATICALLY_DETECT_AND")}
            </p>

            <div className="flex flex-col gap-6 md:flex-row">
              {authAccountData && (
                <div className="min-w-[220px] flex-1">
                  <div className="mb-2 text-sm font-medium text-gray-600">{t("CREATE_PROJECT_RESOURCES_FORM.GITHUB_AUTH_ACCOUNT")}</div>
                  <div className="flex items-center gap-2 rounded-md py-2">
                    <div className="relative flex h-6 w-6 items-center justify-center overflow-hidden rounded-full bg-gray-200">
                      {authAccountData.avatar_url && authAccountData.avatar_url !== "" ? (
                        <img
                          src={authAccountData.avatar_url}
                          alt={authAccountData.name || t("CREATE_PROJECT_RESOURCES_FORM.USER_AVATAR")}
                          className="h-full w-full object-cover"
                        />
                      ) : (
                        <img src={githubIconSrc} alt="github" width={20} height={20} />
                      )}
                    </div>
                    <div className="text-sm font-medium">{authAccountData.login}</div>
                    {authAccountData.name && (
                      <div className="text-sm font-medium">({authAccountData.name})</div>
                    )}
                  </div>
                </div>
              )}

              {(selectedRepositories ?? []).length > 0 && (
                <div className="min-w-[220px] flex-1">
                  <div className="mb-2 text-sm font-medium text-gray-600">
                    {t("CREATE_PROJECT_RESOURCES_FORM.GIT_REPOS", { count: (selectedRepositories ?? []).length })}</div>
                  <ul className="flex flex-col gap-2">
                    {(selectedRepositories ?? []).map((repo: IRepository) => (
                      <li
                        key={repo.id}
                        className="flex items-center justify-between rounded-md bg-card py-2"
                      >
                        <div className="flex items-center gap-2">
                          <img src={githubIconSrc} alt="github" width={20} height={20} />
                          <div className="text-sm font-medium"> {repo.full_name}</div>
                        </div>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
            <div className="mt-4 flex w-full justify-center">
              <Button
                type="button"
                variant="outline"
                className="flex w-full items-center justify-center gap-2"
                onClick={handleAddRepositoryClick}
              >
                <Plus size={16} /> {t("CREATE_PROJECT_RESOURCES_FORM.ADD_REPOSITORY")}
              </Button>
            </div>
          </div>
        </div>
        <div className="mt-10">
          <Button size="lg" disabled={!isValid}>
            {t("common:CONTINUE")}
          </Button>
        </div>

        <Dialog open={repositoryModalOpen} onOpenChange={setRepositoryModalOpen}>
          <DialogContent className="w-[425px] p-6">
            <DialogHeader>
              <DialogTitle>{t("CREATE_PROJECT_RESOURCES_FORM.CONNECT_REPOSITORY")}</DialogTitle>
              <DialogDescription>
                {t("CREATE_PROJECT_RESOURCES_FORM.SELECT_A_GIT_PROVIDER_TO")}
              </DialogDescription>
            </DialogHeader>
            <ProviderButtons
              extraState={formData[0].name}
              destination="/intermediate-page"
              onClose={handleProviderClose}
            />
          </DialogContent>
        </Dialog>

        <RepositorySelectionModal
          open={selectRepositoryModalOpen}
          onOpenChange={setSelectRepositoryModalOpen}
          onSelectRepository={handleSelectRepository}
          selectedRepositories={selectedRepositories}
          title={t("CREATE_PROJECT_RESOURCES_FORM.SELECT_REPOSITORY")}
          description={t("CREATE_PROJECT_RESOURCES_FORM.SELECT_THE_REPOSITORIES_YOU_WANT")}
        />
      </form>
    </Form>
  );
};
