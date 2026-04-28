import { Link } from "react-router-dom";
import { Button } from "@/components/ui-kits/button/button";

export default function ConsoleCreateProject() {
  return (
    <div className="mx-auto flex max-w-2xl flex-col items-center gap-1 text-center">
      <h3 className="mt-32 text-3xl font-bold tracking-tight">Welcome to SELISE Blocks</h3>
      <div className="mt-3 max-w-lg sm:mt-5 lg:max-w-2xl">
        <p className="text-left text-base font-normal leading-7 text-high-emphasis">
          Explore and manage all your projects in one place. With SELISE Blocks, building and
          scaling applications has never been easier. Start by creating a project.
        </p>
      </div>
      <div className="mt-6 grid grid-cols-2 gap-4">
        <Link to="/create-project">
          <Button className="text-sm">Create a project</Button>
        </Link>
        <Button variant="ghost" disabled>
          View documentation
        </Button>
      </div>
    </div>
  );
}
