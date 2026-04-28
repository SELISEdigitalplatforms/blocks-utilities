

import { createContext, useState } from "react";
import { LogsListHeader } from "../logs-header/logs-header";
import { cn } from "@/lib/utils";
import { LogsList } from "../logs-list";
export interface Service {
  id: string;
  label: string;
  serviceName: string;
}

export interface LogFilter {
  search: string;
  startDate: string;
  endDate: string;
  level: string;
}

interface LogsViewerContextType {
  services: Service[];
  selectedService: Service | null;
  changeService: (service: Service) => void;
  pageSize: number;
  filter: Partial<LogFilter> | null;
  setFilter: React.Dispatch<React.SetStateAction<Partial<LogFilter> | null>>;
  resetFilter: () => void;
  predefinedQueries?: string[];
  serviceNames?: string[];
}

const initialContextValue: LogsViewerContextType = {
  services: [],
  selectedService: null,
  changeService: () => {},
  pageSize: 0,
  filter: null,
  setFilter: () => {},
  resetFilter: () => {},
  predefinedQueries: [],
};

// Create context with the initial value
export const LogsViewerContext = createContext<LogsViewerContextType>(initialContextValue);

interface LogsViewerProps {
  services: Service[];
  startDate?: string;
  endDate?: string;
  pageSize?: number;
  className?: string;
  predefinedQueries?: string[];
}

export const LogsViewer = ({
  pageSize = 20,
  services,
  className,
  predefinedQueries,
}: LogsViewerProps) => {
  const [selectedService, setSelectedService] = useState<Service | null>(
    services.length > 0 ? services[0] : null,
  );
  const [filter, setFilter] = useState<Partial<LogFilter> | null>(null);

  const changeService = (service: Service) => {
    setSelectedService(service);
  };

  const resetFilter = () => {
    setFilter(null);
  };

  return (
    <LogsViewerContext.Provider
      value={{
        pageSize,
        services,
        selectedService,
        changeService,
        filter,
        setFilter,
        resetFilter,
        predefinedQueries,
      }}
    >
      <div className={cn("mt-5 flex flex-col gap-6", className)}>
        <LogsListHeader />
        <LogsList
          key={JSON.stringify({
            selectedService,
            filter,
          })}
        />
      </div>
    </LogsViewerContext.Provider>
  );
};
