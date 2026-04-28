import { Moon, Sun } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { useTheme } from "@/hooks/use-theme";

export function ModeToggle() {
  const { setTheme, resolvedTheme } = useTheme();

  return (
    <Button
      onClick={() => setTheme(resolvedTheme === "light" ? "dark" : "light")}
      variant="ghost"
      size="icon"
      className="h-8 w-8 rounded-full border border-transparent transition-all hover:border-[hsl(var(--border-default))] hover:bg-[hsl(var(--accent))] hover:text-[hsl(var(--accent-foreground))] hover:shadow-sm"
    >
      <Moon className="aspect-square w-5 rotate-0 scale-100 transition-all dark:-rotate-90 dark:scale-0" />
      <Sun className="absolute aspect-square w-5 rotate-90 scale-0 transition-all dark:rotate-0 dark:scale-100" />
      <span className="sr-only">Toggle theme</span>
    </Button>
  );
}