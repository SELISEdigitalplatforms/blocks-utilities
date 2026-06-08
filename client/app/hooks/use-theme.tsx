import { useCallback, useEffect } from "react";
import { useAppSettingsStore } from "@seliseblocks/blocks-kit";

type Theme = "light" | "dark" | "system";

function getSystemTheme(): "light" | "dark" {
  if (typeof window === "undefined") return "light";
  return window.matchMedia("(prefers-color-scheme: dark)").matches
    ? "dark"
    : "light";
}

function applyTheme(theme: Theme) {
  const resolved = theme === "system" ? getSystemTheme() : theme;
  document.documentElement.classList.toggle("dark", resolved === "dark");
}

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const theme = useAppSettingsStore((state) => state.settings.theme);

  useEffect(() => {
    applyTheme(theme);
  }, [theme]);

  return <>{children}</>;
}

export function useTheme() {
  const theme = useAppSettingsStore((state) => state.settings.theme);
  const setSettings = useAppSettingsStore((state) => state.setSettings);
  const resolvedTheme = theme === "system" ? getSystemTheme() : theme;

  const setTheme = useCallback(
    (newTheme: Theme) => {
      setSettings({ theme: newTheme });
      applyTheme(newTheme);
    },
    [setSettings],
  );

  useEffect(() => {
    applyTheme(theme);
  }, [theme]);

  useEffect(() => {
    const mq = window.matchMedia("(prefers-color-scheme: dark)");
    const handler = () => {
      if (theme === "system") {
        applyTheme("system");
        setSettings({ theme: "system" });
      }
    };
    mq.addEventListener("change", handler);
    return () => mq.removeEventListener("change", handler);
  }, [setSettings, theme]);

  return { theme, setTheme, resolvedTheme };
}
