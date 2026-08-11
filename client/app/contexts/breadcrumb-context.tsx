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
  const setLabel = context?.setLabel;
  const clearLabel = context?.clearLabel;

  // Update the label when it changes. The hook has to run on every render, so
  // the provider check lives inside the effect rather than around it. The
  // effect depends on the two stable callbacks rather than on the context
  // value, which the provider rebuilds every time a label changes.
  React.useEffect(() => {
    if (!setLabel || !clearLabel) {
      return;
    }
    if (label) {
      setLabel(href, label);
    }
    return () => {
      clearLabel(href);
    };
  }, [href, label, setLabel, clearLabel]);
}

export function useBreadcrumbLabels() {
  const context = useContext(BreadcrumbContext);
  if (!context) {
    return {};
  }
  return context.labels;
}
