import { useLocation } from "react-router-dom";

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

export default useRoutePathSegments;
