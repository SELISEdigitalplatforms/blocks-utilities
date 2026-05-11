import { useState, useEffect } from "react";
import { useLocation } from "react-router-dom";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui-kits/popover/popover";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { cn } from "@/lib/utils";
import { deriveAgentBaseUrl, deriveDeploymentBaseUrl, deriveIdpBaseUrl, deriveLogicBaseUrl, deriveObservabilityBaseUrl, deriveOsBaseUrl, deriveUdsBaseUrl, deriveEurolmBaseUrl } from "@/lib/blocks-url.util";
import { getRuntimeEnv } from "@/lib/runtime-env";

interface BlocksApp {
  key: string;
  label: string;
  description: string;
  url: string;
  icon: React.ReactNode;
}

function IdpIcon() {
  return (
    <svg viewBox="0 0 40 40" fill="none" xmlns="http://www.w3.org/2000/svg" className="h-9 w-9">
      <rect width="40" height="40" rx="10" fill="#1A3C8F" />
      <path
        d="M20 7L10 11v9c0 5.55 4.27 10.74 10 12 5.73-1.26 10-6.45 10-12v-9L20 7z"
        fill="#ffffff"
        opacity="0.9"
      />
      <rect x="16" y="18" width="8" height="7" rx="1.5" fill="#1A3C8F" />
      <circle cx="20" cy="17.5" r="2.5" stroke="#1A3C8F" strokeWidth="1.5" fill="none" />
    </svg>
  );
}

function UilmIcon() {
  return (
    <svg viewBox="0 0 40 40" fill="none" xmlns="http://www.w3.org/2000/svg" className="h-9 w-9">
      <rect width="40" height="40" rx="10" fill="#0E7490" />
      <path
        d="M8 12h16a2 2 0 012 2v8a2 2 0 01-2 2h-3l-3 3v-3H8a2 2 0 01-2-2v-8a2 2 0 012-2z"
        fill="white"
        opacity="0.95"
      />
      <path d="M12 17h8M12 20h5" stroke="#0E7490" strokeWidth="1.5" strokeLinecap="round" />
      <path
        d="M24 21h6a1.5 1.5 0 011.5 1.5v5a1.5 1.5 0 01-1.5 1.5h-1.5l-2 2v-2H24a1.5 1.5 0 01-1.5-1.5v-5A1.5 1.5 0 0124 21z"
        fill="white"
        opacity="0.7"
      />
    </svg>
  );
}

function AiIcon() {
  return (
    <svg viewBox="0 0 40 40" fill="none" xmlns="http://www.w3.org/2000/svg" className="h-9 w-9">
      <rect width="40" height="40" rx="10" fill="#7C3AED" />
      <path
        d="M20 8l2.5 6.5L29 17l-6.5 2.5L20 26l-2.5-6.5L11 17l6.5-2.5L20 8z"
        fill="white"
      />
      <path
        d="M29 26l1.2 3L33 30.2l-2.8 1.2L29 34l-1.2-2.8L25 30.2l2.8-1.2L29 26z"
        fill="white"
        opacity="0.6"
      />
      <path
        d="M12 26l1 2.5 2.5 1-2.5 1L12 33l-1-2.5-2.5-1 2.5-1L12 26z"
        fill="white"
        opacity="0.5"
      />
    </svg>
  );
}

function DataGatewayIcon() {
  return (
    <svg viewBox="0 0 40 40" fill="none" xmlns="http://www.w3.org/2000/svg" className="h-9 w-9">
      <rect width="40" height="40" rx="10" fill="#D97706" />
      <ellipse cx="20" cy="13" rx="8" ry="3.5" fill="white" opacity="0.95" />
      <path
        d="M12 13v5c0 1.93 3.58 3.5 8 3.5s8-1.57 8-3.5v-5"
        stroke="white"
        strokeWidth="1.5"
        fill="none"
        opacity="0.85"
      />
      <path
        d="M12 18v5c0 1.93 3.58 3.5 8 3.5s8-1.57 8-3.5v-5"
        stroke="white"
        strokeWidth="1.5"
        fill="none"
        opacity="0.6"
      />
    </svg>
  );
}

function BlocksOsIcon() {
  return (
    <svg viewBox="0 0 40 40" fill="none" xmlns="http://www.w3.org/2000/svg" className="h-9 w-9">
      <rect width="40" height="40" rx="10" fill="#059669" />
      <rect x="8" y="8" width="24" height="18" rx="2" stroke="white" strokeWidth="1.5" fill="none" />
      <rect x="8" y="28" width="24" height="2" fill="white" opacity="0.8" />
      <circle cx="15" cy="14" r="1.5" fill="white" opacity="0.7" />
      <circle cx="20" cy="14" r="1.5" fill="white" opacity="0.7" />
      <circle cx="25" cy="14" r="1.5" fill="white" opacity="0.7" />
    </svg>
  );
}

function UtilityIcon() {
  return (
    <svg viewBox="0 0 40 40" fill="none" xmlns="http://www.w3.org/2000/svg" className="h-9 w-9">
      <rect width="40" height="40" rx="10" fill="#64748B" />
      <path
        d="M27.5 9a5.5 5.5 0 00-5.24 7.18l-10.5 10.5a2 2 0 002.83 2.83l10.5-10.5A5.5 5.5 0 1027.5 9z"
        fill="white"
        opacity="0.9"
      />
      <circle cx="27.5" cy="14.5" r="2.5" fill="#64748B" />
    </svg>
  );
}

function LogicIcon() {
  return (
    <svg viewBox="0 0 40 40" fill="none" xmlns="http://www.w3.org/2000/svg" className="h-9 w-9">
      <rect width="40" height="40" rx="10" fill="#4F46E5" />
      <rect x="8" y="17" width="6" height="6" rx="1.5" fill="white" opacity="0.9" />
      <rect x="26" y="11" width="6" height="6" rx="1.5" fill="white" opacity="0.9" />
      <rect x="26" y="23" width="6" height="6" rx="1.5" fill="white" opacity="0.9" />
      <path d="M14 20h5l3-6h2" stroke="white" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" opacity="0.85" />
      <path d="M19 20l3 6h2" stroke="white" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" opacity="0.85" />
    </svg>
  );
}

function ObservabilityIcon() {
  return (
    <svg viewBox="0 0 40 40" fill="none" xmlns="http://www.w3.org/2000/svg" className="h-9 w-9">
      <rect width="40" height="40" rx="10" fill="#0891B2" />
      <path
        d="M20 12c-6 0-10 8-10 8s4 8 10 8 10-8 10-8-4-8-10-8z"
        fill="white"
        opacity="0.9"
      />
      <circle cx="20" cy="20" r="3.5" fill="#0891B2" />
      <circle cx="20" cy="20" r="1.5" fill="white" opacity="0.8" />
      <path d="M10 30l4-5M30 30l-4-5" stroke="white" strokeWidth="1.2" strokeLinecap="round" opacity="0.5" />
    </svg>
  );
}

function DeploymentsIcon() {
  return (
    <svg viewBox="0 0 40 40" fill="none" xmlns="http://www.w3.org/2000/svg" className="h-9 w-9">
      <rect width="40" height="40" rx="10" fill="#DC2626" />
      <path
        d="M20 7c-2 4-6 6-9 7l1 8c1 5 5 9 8 10 3-1 7-5 8-10l1-8c-3-1-7-3-9-7z"
        fill="white"
        opacity="0.9"
      />
      <path d="M20 14v8M16 18l4-4 4 4" stroke="#DC2626" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

const SELISE_APPS: BlocksApp[] = [
  {
    key: "idp",
    label: "IDP",
    description: "Identity & Access",
    url: deriveIdpBaseUrl(),
    icon: <IdpIcon />,
  },
  {
    key: "uilm",
    label: "EUROLM",
    description: "Localization",
    url: deriveEurolmBaseUrl(),
    icon: <UilmIcon />,
  },
  {
    key: "ai",
    label: "Blocks Agents",
    description: "AI Platform",
    url: deriveAgentBaseUrl(),
    icon: <AiIcon />,
  },
  {
    key: "data-gateway",
    label: "Data Gateway",
    description: "Data Integration",
    url: deriveUdsBaseUrl(),
    icon: <DataGatewayIcon />,
  },
  {
    key: "blocks-os",
    label: "Blocks OS",
    description: "Operating System",
    url: deriveOsBaseUrl(),
    icon: <BlocksOsIcon />,
  },
  {
    key: "utility",
    label: "Utility",
    description: "Utility Tools",
    url: getRuntimeEnv("BLOCKS_API_BASE_URL") ?? "",
    icon: <UtilityIcon />,
  },
  {
    key: "logic",
    label: "Logic",
    description: "Business Logic",
    url: deriveLogicBaseUrl(),
    icon: <LogicIcon />,
  },
  {
    key: "observability",
    label: "Observability",
    description: "Monitoring & Logs",
    url: deriveObservabilityBaseUrl(),
    icon: <ObservabilityIcon />,
  },
  {
    key: "deployments",
    label: "Deployments",
    description: "CI/CD & Releases",
    url: deriveDeploymentBaseUrl(),
    icon: <DeploymentsIcon />,
  },
];

interface AppTileProps {
  app: BlocksApp;
}

function AppTile({ app }: AppTileProps) {
  return (
    <a
      href={app.url}
      target="_blank"
      rel="noopener noreferrer"
      className="group flex flex-col items-center gap-2 rounded-xl p-3 text-center transition-colors hover:bg-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
    >
      <div className="flex h-12 w-12 items-center justify-center overflow-hidden">
        {app.icon}
      </div>
      <span className="line-clamp-1 max-w-[90px] text-[12px] font-medium leading-tight text-foreground">
        {app.label}
      </span>
    </a>
  );
}

function LauncherTriggerIcon() {
  return (
    <svg
      viewBox="0 0 20 20"
      fill="currentColor"
      xmlns="http://www.w3.org/2000/svg"
      className="h-5 w-5"
    >
      <rect x="1"  y="1"  width="5" height="5" rx="1.5" />
      <rect x="7.5" y="1"  width="5" height="5" rx="1.5" />
      <rect x="14" y="1"  width="5" height="5" rx="1.5" />
      <rect x="1"  y="7.5" width="5" height="5" rx="1.5" />
      <rect x="7.5" y="7.5" width="5" height="5" rx="1.5" />
      <rect x="14" y="7.5" width="5" height="5" rx="1.5" />
      <rect x="1"  y="14" width="5" height="5" rx="1.5" />
      <rect x="7.5" y="14" width="5" height="5" rx="1.5" />
      <rect x="14" y="14" width="5" height="5" rx="1.5" />
    </svg>
  );
}

function EditIcon() {
  return (
    <svg
      viewBox="0 0 20 20"
      fill="currentColor"
      xmlns="http://www.w3.org/2000/svg"
      className="h-4 w-4"
    >
      <path d="M13.586 3.586a2 2 0 112.828 2.828l-.793.793-2.828-2.828.793-.793zM11.379 5.793L3 14.172V17h2.828l8.38-8.379-2.83-2.828z" />
    </svg>
  );
}

function StarIcon({ filled }: { filled: boolean }) {
  return (
    <svg
      viewBox="0 0 20 20"
      xmlns="http://www.w3.org/2000/svg"
      className="h-5 w-5"
      fill={filled ? "currentColor" : "none"}
      stroke="currentColor"
      strokeWidth={filled ? 0 : 1.5}
    >
      <path d="M10 1.5l2.38 6.29h6.63l-5.36 4.12 2.04 6.29-5.69-4.14-5.69 4.14 2.04-6.29-5.36-4.12h6.63z" />
    </svg>
  );
}

export function BlocksAppLauncher() {
  const [open, setOpen] = useState(false);
  const [editDialogOpen, setEditDialogOpen] = useState(false);
  const [favouriteKeys, setFavouriteKeys] = useState<Set<string>>(new Set());
  const [isHydrated, setIsHydrated] = useState(false);
  const location = useLocation();
  // const isAllowedRoute = !location.pathname.includes("/console") && !location.pathname.includes("/project-overview") && !location.pathname.includes("/services/lmt/logs");
  useEffect(() => {
    const stored = localStorage.getItem("blocks-app-favourites");
    const keys = stored
      ? new Set<string>(JSON.parse(stored) as string[])
      : new Set<string>(["idp", "uilm"]);
    setFavouriteKeys(keys);
    setIsHydrated(true);
  }, []);
  const saveFavourites = (keys: Set<string>) => {
    setFavouriteKeys(keys);
    localStorage.setItem("blocks-app-favourites", JSON.stringify(Array.from(keys)));
  };
  const toggleFavourite = (key: string) => {
    const newFavourites = new Set(favouriteKeys);
    if (newFavourites.has(key)) {
      newFavourites.delete(key);
    } else {
      newFavourites.add(key);
    }
    saveFavourites(newFavourites);
  };
  // if (!isHydrated || !isAllowedRoute) return null;
  if (!isHydrated) return null;
  const favourites = SELISE_APPS.filter((a) => favouriteKeys.has(a.key));
  const moreApps = SELISE_APPS.filter((a) => !favouriteKeys.has(a.key));
  return (
    <>
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <button
            aria-label="SELISE Blocks apps"
            className={cn(
              "flex h-9 w-9 items-center justify-center rounded-full text-muted-foreground transition-colors",
              "hover:bg-accent hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
              open && "bg-accent text-foreground"
            )}
          >
            <LauncherTriggerIcon />
          </button>
        </PopoverTrigger>
        <PopoverContent
          align="end"
          sideOffset={8}
          className="w-[260px] overflow-hidden rounded-2xl p-0 shadow-xl"
        >
          <div className="flex items-center justify-between bg-background px-3 py-3 border-b">
            <p className="text-[13px] font-semibold text-foreground">Your favourites</p>
            <button
              onClick={() => setEditDialogOpen(true)}
              className="flex h-6 w-6 items-center justify-center rounded text-muted-foreground hover:bg-accent hover:text-foreground transition-colors"
              aria-label="Edit favourites"
            >
              <EditIcon />
            </button>
          </div>
          <div className="px-3 pb-2 pt-3">
            <div className="grid grid-cols-3">
              {favourites.map((app) => (
                <AppTile key={app.key} app={app} />
              ))}
            </div>
          </div>
          {moreApps.length > 0 && (
            <div className="bg-muted/50 px-3 pb-4 pt-3 border-t">
              <p className="mb-2 px-1 text-[13px] font-semibold text-muted-foreground">
                More from SELISE Blocks
              </p>
              <div className="grid grid-cols-3">
                {moreApps.map((app) => (
                  <AppTile key={app.key} app={app} />
                ))}
              </div>
            </div>
          )}
        </PopoverContent>
      </Popover>
      <Dialog open={editDialogOpen} onOpenChange={setEditDialogOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Manage Favourites</DialogTitle>
          </DialogHeader>
          <div className="grid grid-cols-2 gap-4 py-2">
            {SELISE_APPS.map((app) => (
              <button
                key={app.key}
                onClick={() => toggleFavourite(app.key)}
                className={cn(
                  "group flex flex-col items-center gap-2 rounded-xl border border-transparent bg-muted/40 p-4 shadow-sm transition-all hover:bg-accent hover:shadow-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary",
                  favouriteKeys.has(app.key) && "border-primary bg-primary/10"
                )}
                aria-pressed={favouriteKeys.has(app.key)}
              >
                <span className="flex items-center justify-center h-12 w-12 mb-1">
                  {app.icon}
                </span>
                <span className="font-semibold text-sm text-foreground mb-0.5 line-clamp-1">{app.label}</span>
                <span className="text-xs text-muted-foreground text-center line-clamp-2">{app.description}</span>
              </button>
            ))}
          </div>
        </DialogContent>
      </Dialog>
    </>
  );
}
