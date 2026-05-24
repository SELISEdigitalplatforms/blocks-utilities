import { useTheme } from "@/hooks/use-theme";

interface LogoProps {
  src?: string;
  alt?: string;
  width?: number;
  height?: number;
  className?: string;
  variant?: "logo" | "icon";
}

export function Logo({
  src,
  alt,
  width,
  height,
  className,
  variant = "logo",
}: LogoProps) {
  const { resolvedTheme } = useTheme();

  if (src) {
    return (
      <img
        src={src}
        alt={alt ?? "SELISE Logo"}
        width={width}
        height={height}
        className={className}
      />
    );
  }

  const logoSrc =
    variant === "icon"
      ? resolvedTheme === "dark"
        ? "/Icon_White.svg"
        : "/Icon_Black.svg"
      : resolvedTheme === "dark"
        ? "/Logo_White.svg"
        : "/Logo_Black.svg";

  return (
    <img
      src={logoSrc}
      alt={alt ?? "SELISE Logo"}
      width={width}
      height={height}
      className={className}
    />
  );
}
