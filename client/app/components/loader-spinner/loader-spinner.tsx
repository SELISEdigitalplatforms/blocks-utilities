import React from "react";
import { Loader } from "lucide-react";

interface LoadingSpinnerProps {
  size?: number;
  color?: string;
  fullScreen?: boolean;
}

const LoadingSpinner: React.FC<LoadingSpinnerProps> = ({
  size = 100,
  color = "text-red-400",
  fullScreen = true,
}) => {
  return (
    <div className={`flex ${fullScreen ? "h-screen w-full" : ""} items-center justify-center`}>
      <div className="flex animate-pulse">
        <div className="flex items-center justify-center">
          <Loader size={size} className={`animate-spin ${color}`} />
        </div>
      </div>
    </div>
  );
};

export default LoadingSpinner;
