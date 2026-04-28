import { useState } from "react";
import { ChevronRight, Hourglass } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { Card, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { IProject } from "@blocks-identifier/models/project.model";
import { useProjectStore } from "@/store/useProjectStore";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
import { environmentOptions } from "@/constants/environment-options";

type EnvironmentCardProps = {
  project: IProject;
  isMigrationOngoing?: boolean;
  className?: string;
};

export const EnvironmentCard = ({
  project,
  isMigrationOngoing,
  className,
}: EnvironmentCardProps) => {
  const navigate = useNavigate();
  const { setSelectedProject } = useProjectStore();
  const [isConfirmationOpen, setIsConfirmationOpen] = useState(false);

  const onClickHandler = (): void => {
    setSelectedProject(project);
    navigate("/dashboard");
  };

  const handleCardClick = (): void => {
    if (isMigrationOngoing) {
      setIsConfirmationOpen(true);
      return;
    }
    onClickHandler();
  };

  const handleConfirm = (): void => {
    setIsConfirmationOpen(false);
    onClickHandler();
  };

  return (
    <Dialog open={isConfirmationOpen} onOpenChange={setIsConfirmationOpen}>
      <Card
        onClick={handleCardClick}
        className={`group flex h-[60px] cursor-pointer flex-col justify-between rounded-sm p-4 shadow-none transition-shadow duration-200 hover:shadow-md ${className}`}
      >
        <CardHeader className="flex flex-row justify-between !p-0">
          <CardTitle className="line-clamp-1 break-all text-lg leading-tight">
            <div className="flex w-fit flex-row items-center gap-1">
              <div className="text-base text-medium-emphasis">
                {environmentOptions.find((option) => option.value === project?.environment)?.label}
              </div>
              {isMigrationOngoing && (
                <TooltipProvider>
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <Hourglass className="h-4 w-4 cursor-pointer text-icon-warning" />
                    </TooltipTrigger>
                    <TooltipContent className="border-none bg-neutral-500 text-white shadow-none">
                      Migration in progress
                    </TooltipContent>
                  </Tooltip>
                </TooltipProvider>
              )}
            </div>
          </CardTitle>
          <ChevronRight className="mt-1 h-4 w-4 opacity-0 transition-opacity duration-200 group-hover:opacity-100" />
        </CardHeader>
      </Card>
      {isMigrationOngoing && (
        <ConfirmationModal
          onCancel={() => setIsConfirmationOpen(false)}
          onConfirm={handleConfirm}
          data={{
            dialogTitle: "Environment Migration in Progress",
            dialogSubtitle:
              "This environment is currently migrating. Any changes now may cause incomplete data or service interruptions. Proceed only if necessary.",
            confirmButton: "Continue Anyway",
            cancelButton: "Cancel",
          }}
        />
      )}
    </Dialog>
  );
};
