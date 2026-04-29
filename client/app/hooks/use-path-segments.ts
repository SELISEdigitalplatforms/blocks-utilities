import { useLocation } from "react-router-dom";
import { useRef, useEffect, useState } from "react";

const useRoutePathSegments = () => {
  const { pathname } = useLocation();
  const pathArray = pathname.split("/").filter((path) => path);

  const breadcrumbs = pathArray.map((path, index) => {
    const href = "/" + pathArray.slice(0, index + 1).join("/");
    return {
      href,
      label: formateLabel(path),
    };
  });
  return breadcrumbs;
};

const formateLabel = (label: string): string => {
  const words = label.split("-");
  const formattedWords = words.map((word) => {
    return word.charAt(0).toUpperCase() + word.slice(1);
  });
  return formattedWords.join(" ");
};

export const usePreviousPath = () => {
  const location = useLocation();
  const [previousPath, setPreviousPath] = useState<string | null>(null);
  const previousPathRef = useRef<string | null>(null);

  useEffect(() => {
    const currentPath = location.pathname;
    if (previousPathRef.current !== null && previousPathRef.current !== currentPath) {
      setPreviousPath(previousPathRef.current);
    }
    previousPathRef.current = currentPath;
  }, [location.pathname]);

  return previousPath;
};

export default useRoutePathSegments;
