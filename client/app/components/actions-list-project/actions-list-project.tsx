import { useGetProject } from "@/hooks/use-project";
import { useProjectStore } from "@/store/useProjectStore";
import { ArchivedProject } from "@/components/archive-project/archive-project";
import { EditProject } from "@/components/edit-project/edit-project";
import { useGetUser } from "@blocks-idp/iam/hooks/use-user";

export const ActionsListProject = () => {
  const { itemId } = useProjectStore().selectedProject || { itemId: "", tenantId: "" };
  const { data, isLoading, isFetching } = useGetProject({ projectId: itemId });
  const { data: loggedInUser } = useGetUser();

  const isOwner = data?.data?.createdBy === loggedInUser?.data?.itemId;

  return (
    <div className="flex items-center gap-2">
      {!isLoading && !isFetching && !data?.data?.isDisabled && isOwner && <ArchivedProject />}
      {!data?.data?.isDisabled && (
        <>{!isLoading && !isFetching && <EditProject data={data} isLoading={isLoading} />}</>
      )}
    </div>
  );
};
