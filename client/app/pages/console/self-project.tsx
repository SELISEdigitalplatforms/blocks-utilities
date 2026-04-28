import { useGetProjects } from "@/hooks/use-project";
import ConsoleCreateProject from "@/components/console-create/console-create";
import { ProjectCard } from "@/components/project-card/project-card";
import { ProjectCardLoading } from "@/components/project-card/loading";
import { AddProjectCard } from "@/components/project-card/add-project-card";

const SelfProjectLoading = () => {
  return (
    <>
      <div className="mt-4 flex items-center justify-between">
        <h4 className="text-xl font-semibold">Blocks projects</h4>
      </div>
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
        {Array(8)
          .fill(1, 0, 8)
          .map((_item, index) => (
            <ProjectCardLoading key={index} />
          ))}
      </div>
    </>
  );
};

export const SelfProject = () => {
  const { data, isLoading, isFetching } = useGetProjects();

  if (isLoading || isFetching) return <SelfProjectLoading />;
  const projectGroups = data || [];
  if (!projectGroups.length) return <ConsoleCreateProject />;

  return (
    <>
      <div className="mt-4 flex flex-col justify-between md:flex-row md:items-center">
        <h4 className="text-xl font-semibold">Your Blocks projects</h4>
        {projectGroups.length > 9 && (
          <div className="text-medium-emphasis">
            Please delete an existing project to create a new one.
          </div>
        )}
      </div>
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
        {projectGroups.length < 10 && <AddProjectCard />}
        {projectGroups.map((project) => (
          <ProjectCard
            key={project.tenantGroupId}
            project={project.projects[0]}
            projects={project.projects}
          />
        ))}
      </div>
    </>
  );
};
