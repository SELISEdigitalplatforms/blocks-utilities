import React, { createContext, useContext, useState, useCallback } from "react";

interface DynamicBreadcrumbLabels {
  [href: string]: string;
}

interface BreadcrumbContextValue {
  labels: DynamicBreadcrumbLabels;
  setLabel: (href: string, label: string) => void;
  clearLabel: (href: string) => void;
  clearAll: () => void;
}

const BreadcrumbContext = createContext<BreadcrumbContextValue | null>(null);

export function BreadcrumbProvider({ children }: { children: React.ReactNode }) {
  const [labels, setLabels] = useState<DynamicBreadcrumbLabels>({});

  const setLabel = useCallback((href: string, label: string) => {
    setLabels((prev) => ({ ...prev, [href]: label }));
  }, []);

  const clearLabel = useCallback((href: string) => {
    setLabels((prev) => {
      const newLabels = { ...prev };
      delete newLabels[href];
      return newLabels;
    });
  }, []);

  const clearAll = useCallback(() => {
    setLabels({});
  }, []);

  return (
    <BreadcrumbContext.Provider value={{ labels, setLabel, clearLabel, clearAll }}>
      {children}
    </BreadcrumbContext.Provider>
  );
}

export function useDynamicBreadcrumbLabel(href: string, label: string) {
  const context = useContext(BreadcrumbContext);
  if (context) {
    // Update the label when it changes
    React.useEffect(() => {
      if (label) {
        context.setLabel(href, label);
      }
      return () => {
        context.clearLabel(href);
      };
    }, [href, label, context]);
  }
}

export function useBreadcrumbLabels() {
  const context = useContext(BreadcrumbContext);
  if (!context) {
    return {};
  }
  return context.labels;
}
