import { motion } from "framer-motion";
import { useGetProjects } from "@blocks-identifier/hooks/use-project";
import ConsoleCreateProject from "@/components/console-create/console-create";
import { ProjectCard } from "@/components/project-card/project-card";
import { ProjectCardLoading } from "@/components/project-card/loading";
// import { AddProjectCard } from "@/components/project-card/add-project-card";

const cardVariants = {
  hidden: { opacity: 0, y: 20, scale: 0.97 },
  visible: (i: number) => ({
    opacity: 1,
    y: 0,
    scale: 1,
    transition: {
      delay: i * 0.06,
      duration: 0.4,
      ease: [0.22, 1, 0.36, 1] as [number, number, number, number],
    },
  }),
};

const SelfProjectLoading = () => {
  return (
    <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
      {Array(8)
        .fill(null)
        .map((_item, index) => (
          <ProjectCardLoading key={index} />
        ))}
    </div>
  );
};

export const SelfProject = () => {
  const { data, isLoading, isFetching } = useGetProjects();

  if (isLoading || isFetching) return <SelfProjectLoading />;
  const projectGroups = data || [];
  if (!projectGroups.length) return <ConsoleCreateProject />;

  return (
    <section className="flex flex-col gap-4">
      <div className="flex items-center justify-between gap-3">
        <div className="flex min-w-0 flex-1 items-center gap-3">
          <h2 className="shrink-0 text-base font-semibold text-[hsl(var(--high-emphasis))]">
            Your Blocks Projects
          </h2>
          <span className="rounded-full bg-primary/10 px-2 py-0.5 text-xs font-semibold text-primary">
            {projectGroups.length}
          </span>
        </div>
        {projectGroups.length > 9 && (
          <span className="shrink-0 text-sm text-[hsl(var(--medium-emphasis))]">
            Please delete an existing project to create a new one.
          </span>
        )}
      </div>
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
        {/* {projectGroups.length < 10 && (
          <motion.div variants={cardVariants} custom={0} initial="hidden" animate="visible">
            <AddProjectCard />
          </motion.div>
        )} */}
        {projectGroups.map((project, i) => (
          <motion.div
            key={project.tenantGroupId}
            variants={cardVariants}
            custom={projectGroups.length < 10 ? i + 1 : i}
            initial="hidden"
            animate="visible"
          >
            <ProjectCard
              project={project.projects[0]}
              projects={project.projects}
            />
          </motion.div>
        ))}
      </div>
    </section>
  );
};
