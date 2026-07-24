/**
 * Test-only stub for `@seliseblocks/blocks-kit` (and its `/lib`, `/providers`,
 * `/hooks`, `/layouts` subpaths).
 *
 * The real design-system barrel eagerly imports framer-motion, whose
 * `motion-utils` reads `process.env.NODE_ENV` at import time and throws under
 * the jsdom test environment. Aliasing the package to this stub keeps the heavy
 * animation deps out of the test module graph while still providing the exports
 * the app modules under test rely on.
 *
 * Design choice: the UI primitives the app imports from the kit (Button, Card,
 * Dialog, Form, Input, ...) are re-exported from this repo's own, self-contained
 * `@/components/ui-kits/*` implementations (radix based, no framer-motion). That
 * gives real, behavioral primitives so tests can drive submits, validation and
 * open/close state instead of shallow passthroughs. Icons come from lucide-react
 * and form/date helpers from the real libraries. Only genuinely kit-owned
 * widgets that have no local equivalent are provided as light stand-ins.
 *
 * This is test infrastructure only. It never replaces the repo's own source
 * under test, and it is excluded from coverage via the test-utils glob.
 */
import React from "react";
import { create } from "zustand";

/* ----------------------------------------------------------------------------
 * Stores
 * ------------------------------------------------------------------------- */

export { useAuthStore } from "@/store/useAuthStore";
export { useLanguageViewStore } from "@/cross-modules/localization/store/use-language-view-store";

// The app consumes `useProjectStore()` as a zustand store returning
// { selectedProject, selectedTenantGroup, setProjects, setSelectedProject,
//   setTenantGroup, projects }. The real store lives in the kit; here we back
// it with a genuine zustand store so tests can seed it via
// `useProjectStore.setState({ selectedProject: {...} })`.
type ProjectStoreState = {
  projects: unknown[];
  selectedProject: Record<string, unknown> | null;
  selectedTenantGroup: string | null;
  setProjects: (projects: unknown[]) => void;
  setSelectedProject: (project: Record<string, unknown> | null) => void;
  setTenantGroup: (group: string | null) => void;
};

export const useProjectStore = create<ProjectStoreState>((set) => ({
  projects: [],
  selectedProject: null,
  selectedTenantGroup: null,
  setProjects: (projects) => set({ projects }),
  setSelectedProject: (selectedProject) => set({ selectedProject }),
  setTenantGroup: (selectedTenantGroup) => set({ selectedTenantGroup }),
}));

type AppSettingsState = {
  settings: { theme: string };
  setSettings: (settings: { theme: string }) => void;
};

export const useAppSettingsStore = create<AppSettingsState>((set) => ({
  settings: { theme: "light" },
  setSettings: (settings) => set({ settings }),
}));

/* ----------------------------------------------------------------------------
 * Helpers / utils (re-exported from the real local modules or libraries)
 * ------------------------------------------------------------------------- */

export {
  cn,
  formatDate,
  formatFullDate,
  parseDateString,
  compareDates,
  clearBreadCrumbTitleEntry,
  debounce,
  parseMongoDBString,
  checkValidDate,
  deepEqual,
  clearQueryString,
  getUniqueID,
  formatSize,
} from "@/lib/utils";
export { getRuntimeEnv } from "@/lib/runtime-env";
export { getApiPath, getApiUrl } from "@/lib/get-api-path";
export {
  getErrorMessage,
  isErrorWithErrors,
  handleErrorMessages,
} from "@/lib/error";
export {
  useToast,
  toast,
  showSuccessToast,
  showInfoToast,
  showErrorToast,
} from "@/hooks/use-toast";

export { format } from "date-fns";
export { zodResolver } from "@hookform/resolvers/zod";
export { z } from "zod";
export * from "react-hook-form";

// tanstack/react-table re-exports the kit surfaces for tables.
export {
  flexRender,
  getCoreRowModel,
  getFilteredRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useReactTable,
} from "@tanstack/react-table";
export type { ColumnDef } from "@tanstack/react-table";

// Icons: the kit re-exports lucide-react. The star-export makes every icon the
// app references available; the explicit primitive exports below win on any
// name collision (e.g. Calendar the date-picker vs. the lucide icon).
export * from "lucide-react";

/* ----------------------------------------------------------------------------
 * UI primitives (real local implementations)
 * ------------------------------------------------------------------------- */

export { Button, buttonVariants } from "@/components/ui-kits/button/button";
export {
  Card,
  CardHeader,
  CardFooter,
  CardTitle,
  CardDescription,
  CardContent,
} from "@/components/ui-kits/card/card";
export {
  Dialog,
  DialogPortal,
  DialogOverlay,
  DialogClose,
  DialogTrigger,
  DialogContent,
  DialogHeader,
  DialogFooter,
  DialogTitle,
  DialogDescription,
} from "@/components/ui-kits/dialog/dialog";
export {
  useFormField,
  Form,
  FormItem,
  FormLabel,
  FormControl,
  FormDescription,
  FormMessage,
  FormField,
} from "@/components/ui-kits/form/form";
export { Input } from "@/components/ui-kits/input/input";
export { Textarea } from "@/components/ui-kits/textarea/textarea";
export { Label } from "@/components/ui-kits/label/label";
export {
  Select,
  SelectGroup,
  SelectValue,
  SelectTrigger,
  SelectContent,
  SelectLabel,
  SelectItem,
  SelectSeparator,
  SelectScrollUpButton,
  SelectScrollDownButton,
} from "@/components/ui-kits/select/select";
export {
  DropdownMenu,
  DropdownMenuTrigger,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuCheckboxItem,
  DropdownMenuRadioItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuShortcut,
  DropdownMenuGroup,
  DropdownMenuPortal,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuRadioGroup,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
export {
  Table,
  TableHeader,
  TableBody,
  TableFooter,
  TableHead,
  TableRow,
  TableCell,
  TableCaption,
} from "@/components/ui-kits/table/table";
export {
  Tooltip,
  TooltipTrigger,
  TooltipContent,
  TooltipProvider,
} from "@/components/ui-kits/tooltip/tooltip";
export {
  Popover,
  PopoverTrigger,
  PopoverContent,
} from "@/components/ui-kits/popover/popover";
export { Calendar } from "@/components/ui-kits/calendar/calendar";
export { Switch } from "@/components/ui-kits/switch/switch";
export { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
export { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
export { Badge, badgeVariants } from "@/components/ui-kits/badge/badge";
export { Pagination } from "@/components/ui-kits/pagination/pagination";
export {
  Tabs,
  TabsList,
  TabsTrigger,
  TabsContent,
} from "@/components/ui-kits/tabs/tabs";
export {
  Drawer,
  DrawerPortal,
  DrawerOverlay,
  DrawerTrigger,
  DrawerClose,
  DrawerContent,
  DrawerHeader,
  DrawerFooter,
  DrawerTitle,
  DrawerDescription,
} from "@/components/ui-kits/drawer/drawer";
export {
  RadioGroup,
  RadioGroupItem,
} from "@/components/ui-kits/radio-group/radio-group";
export {
  ScrollArea,
  ScrollBar,
} from "@/components/ui-kits/scroll-area/scroll-area";
export {
  InputOTP,
  InputOTPGroup,
  InputOTPSlot,
  InputOTPSeparator,
} from "@/components/ui-kits/input-otp/input-otp";

// Widgets that live locally and are also surfaced through the kit barrel.
export { CopyToClipboardButton } from "@/components/copy-to-clipboard-button/copy-to-clipboard-button";
export { MaskedText } from "@/components/masked-text/masked-text";
export {
  FileUploader,
  FileInput,
  FileUploaderContent,
  FileUploaderItem,
} from "@/components/file-uploader/file-uploader";

/* ----------------------------------------------------------------------------
 * Kit-owned widgets / hooks with no local equivalent (light stand-ins)
 * ------------------------------------------------------------------------- */

type PassthroughProps = { children?: React.ReactNode } & Record<string, unknown>;

const passthrough = (label: string) => {
  const Component = ({ children }: PassthroughProps) =>
    React.createElement(React.Fragment, null, children);
  Component.displayName = label;
  return Component;
};

export const BlocksAppLayout = passthrough("BlocksAppLayout");
export const ConsoleLayout = passthrough("ConsoleLayout");
export const ConsolePage = passthrough("ConsolePage");
export const DashboardOverview = passthrough("DashboardOverview");
export const DashboardRoute = passthrough("DashboardRoute");
export const LoginPage = passthrough("LoginPage");
export const CallbackPage = passthrough("CallbackPage");
export const ProfilePage = passthrough("ProfilePage");
export const AuthResolver = passthrough("AuthResolver");
export const ProtectedGuard = passthrough("ProtectedGuard");
export const PublicGuard = passthrough("PublicGuard");
export const ThemeProvider = passthrough("ThemeProvider");
export const Toaster = passthrough("Toaster");
export const NuqsAdapter = passthrough("NuqsAdapter");
export const EnvironmentCard = passthrough("EnvironmentCard");
export const FilterControls = passthrough("FilterControls");
export const PrimaryButton = ({ children, ...rest }: PassthroughProps) =>
  React.createElement("button", rest as Record<string, unknown>, children);
PrimaryButton.displayName = "PrimaryButton";

// Hooks with no local equivalent, given deterministic test defaults.
export const useScopedPath = () => (path: string) => `/${path}`;
export const useGetProjects = () => ({
  data: [],
  isLoading: false,
  isFetching: false,
  refetch: () => Promise.resolve(),
});
export const useValidateAuthorization = () => ({
  data: true,
  refetch: () => Promise.resolve(),
});
export const useStartImpersonation = () => ({
  mutate: () => {},
  mutateAsync: () => Promise.resolve(),
  isPending: false,
});
export const useLogout = () => ({
  mutate: () => {},
  mutateAsync: () => Promise.resolve(),
  isPending: false,
});
export const getQueryClient = () => ({
  invalidateQueries: () => Promise.resolve(),
  removeQueries: () => {},
  clear: () => {},
});

/* ----------------------------------------------------------------------------
 * HttpClient (constructed at import time by some service modules)
 * ------------------------------------------------------------------------- */

type HttpClientOptions = { baseURL?: string; blocksKey?: string };

export class HttpClient {
  baseURL: string;
  blocksKey: string;
  constructor(options: HttpClientOptions = {}) {
    this.baseURL = options.baseURL ?? "";
    this.blocksKey = options.blocksKey ?? "";
  }
  get<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
  post<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
  put<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
  patch<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
  delete<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
  stream<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
}

export class HttpError extends Error {
  status: number;
  errors: Record<string, string | string[]>;
  constructor(status = 0, errors: Record<string, string | string[]> = {}) {
    super(`HttpError ${status}`);
    this.status = status;
    this.errors = errors;
  }
}

/* ----------------------------------------------------------------------------
 * Types (erased at build; declared so value-position imports resolve)
 * ------------------------------------------------------------------------- */

export type IProject = Record<string, unknown>;
export type MagicUrl = Record<string, unknown>;
