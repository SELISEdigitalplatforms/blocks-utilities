import React, { createContext, useCallback, useState } from "react";
import { useLocation } from "react-router";
import useIsMobile from "@/hooks/use-is-mobile";

type SidebarContextValue = {
  isSidebarOpen: boolean;
  toggleSidebar: () => void;
  closeSidebar: () => void;
  closeWithoutPersist: () => void;
  isSidebarSubMenuOpen: boolean;
  toggleSidebarSubMenu: () => void;
  showSidebarSubMenu: () => void;
  subMenuId: string | null;
  updateSubMenuId: (id: string) => void;
  servicesSearchTerm: string;
  updateServicesSearchTerm: (term: string) => void;
};

const defaultContextValue: SidebarContextValue = {
  isSidebarOpen: false,
  toggleSidebar: () => undefined,
  closeSidebar: () => undefined,
  closeWithoutPersist: () => undefined,
  isSidebarSubMenuOpen: false,
  toggleSidebarSubMenu: () => undefined,
  showSidebarSubMenu: () => undefined,
  subMenuId: null,
  updateSubMenuId: () => undefined,
  servicesSearchTerm: "",
  updateServicesSearchTerm: () => undefined,
};

export const SidebarContext = createContext<SidebarContextValue>(defaultContextValue);

export function DashboardLayoutProvider({
  children,
  isOpen,
  isSubMenuOpen = false,
  storageKey = "sidebar-open",
  persist = false,
}: {
  children: React.ReactNode;
  isOpen: boolean;
  isSubMenuOpen?: boolean;
  storageKey?: string;
  persist?: boolean;
}) {
  const isMobile = useIsMobile();
  const { pathname } = useLocation();
  // Where the sidebar starts: a persisted desktop choice wins, mobile always starts closed,
  // and without persistence it simply follows the viewport.
  const readInitialOpen = (): boolean => {
    if (persist) {
      if (isMobile) return false;
      const stored = localStorage.getItem(storageKey);
      return stored !== null ? (JSON.parse(stored) as boolean) : isOpen;
    }
    return !isMobile;
  };
  const [isSidebarOpen, setIsSidebarOpen] = useState<boolean>(readInitialOpen);
  // The stored choice is read the first time persistence is on, and only then.
  const [hasReadStoredOpen, setHasReadStoredOpen] = useState(persist);

  const [isSidebarSubMenuOpen, setIsSidebarSubMenuOpen] = useState(() => {
    let open = isSubMenuOpen;
    // On a services route the submenu opens on desktop...
    if (!isMobile && pathname.startsWith("/services")) open = true;
    // ...unless the sidebar itself is open, which takes its place.
    if ((isOpen || isSidebarOpen) && !isMobile) open = false;
    return open;
  });
  const [subMenuId, setSubMenuId] = useState<string | null>(() =>
    localStorage.getItem("subMenuId"),
  );
  const [servicesSearchTerm, setServicesSearchTerm] = useState("");

  // React to viewport, route, persistence and sidebar changes while rendering, so the
  // adjusted state is in place before anything is painted. The rules are applied in the
  // same order the provider has always used.
  const [prevInputs, setPrevInputs] = useState({
    isMobile,
    pathname,
    persist,
    isSidebarOpen,
  });
  if (
    prevInputs.isMobile !== isMobile ||
    prevInputs.pathname !== pathname ||
    prevInputs.persist !== persist ||
    prevInputs.isSidebarOpen !== isSidebarOpen
  ) {
    const viewportChanged = prevInputs.isMobile !== isMobile;
    const persistChanged = prevInputs.persist !== persist;
    setPrevInputs({ isMobile, pathname, persist, isSidebarOpen });

    let nextOpen = isSidebarOpen;
    if (persist && !hasReadStoredOpen) {
      setHasReadStoredOpen(true);
      if (!isMobile) {
        const stored = localStorage.getItem(storageKey);
        if (stored !== null) nextOpen = JSON.parse(stored) as boolean;
      } else {
        nextOpen = false;
      }
    }
    if (viewportChanged || persistChanged) {
      if (!persist) {
        nextOpen = !isMobile;
      } else if (isMobile) {
        nextOpen = false;
      }
    }
    if (nextOpen !== isSidebarOpen) setIsSidebarOpen(nextOpen);

    let nextSubMenuOpen = isSidebarSubMenuOpen;
    if (
      (viewportChanged || prevInputs.pathname !== pathname) &&
      !isMobile &&
      pathname.startsWith("/services")
    ) {
      nextSubMenuOpen = true;
    }
    if (
      (viewportChanged || prevInputs.isSidebarOpen !== isSidebarOpen) &&
      isSidebarOpen &&
      !isMobile
    ) {
      nextSubMenuOpen = false;
    }
    if (nextSubMenuOpen !== isSidebarSubMenuOpen) {
      setIsSidebarSubMenuOpen(nextSubMenuOpen);
    }
  }

  const toggleSidebar = useCallback(() => {
    setIsSidebarOpen((prev) => {
      const nextState = !prev;
      if (persist && !isMobile) {
        localStorage.setItem(storageKey, JSON.stringify(nextState));
      }
      if (nextState) {
        setIsSidebarSubMenuOpen(false);
      }
      return nextState;
    });
  }, [isMobile, persist, storageKey]);

  const closeSidebar = useCallback(() => {
    setIsSidebarOpen(false);
    if (persist && !isMobile) {
      localStorage.setItem(storageKey, JSON.stringify(false));
    }
  }, [isMobile, persist, storageKey]);

  const closeWithoutPersist = useCallback(() => {
    setIsSidebarOpen(false);
  }, []);

  const toggleSidebarSubMenu = useCallback(() => {
    setIsSidebarSubMenuOpen((prev) => !prev);
  }, []);

  const showSidebarSubMenu = useCallback(() => {
    setIsSidebarSubMenuOpen(true);
  }, []);

  const updateSubMenuId = useCallback((id: string) => {
    localStorage.setItem("subMenuId", id);
    setSubMenuId(id);
    setServicesSearchTerm("");
  }, []);

  const updateServicesSearchTerm = useCallback((term: string) => {
    setServicesSearchTerm(term);
  }, []);

  return (
    <SidebarContext.Provider
      value={{
        isSidebarOpen,
        toggleSidebar,
        closeSidebar,
        closeWithoutPersist,
        isSidebarSubMenuOpen,
        toggleSidebarSubMenu,
        showSidebarSubMenu,
        subMenuId,
        updateSubMenuId,
        servicesSearchTerm,
        updateServicesSearchTerm,
      }}
    >
      {children}
    </SidebarContext.Provider>
  );
}