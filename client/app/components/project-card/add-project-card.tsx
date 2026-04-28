import { Plus } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { Card, CardContent } from "@/components/ui-kits/card/card";

export const AddProjectCard = () => {
  const navigate = useNavigate();

  return (
    <Card
      onClick={() => navigate("/create-project")}
      className="flex h-[160px] cursor-pointer items-center justify-center rounded-xl border border-primary/30 shadow-sm md:py-4 transition-all duration-200 bg-transparent hover:border-primary/70 hover:shadow-md"
    >
      <CardContent className="p-0 text-center">
        <div className="flex justify-center">
          <Plus className="text-primary" strokeWidth={2} size={50} />
        </div>
        <p className="mt-2 font-bold text-primary">Add Project</p>
      </CardContent>
    </Card>
  );
};
