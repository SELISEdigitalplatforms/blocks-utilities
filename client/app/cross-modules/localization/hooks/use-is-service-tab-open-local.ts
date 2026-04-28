import { useState, useEffect } from "react";

const useIsServiceBarOpenLocal = (breakpoint = 1134) => {
  const [isServiceBarOpen, setIsServiceBarOpen] = useState(false);
  
  useEffect(() => {
    const handleResize = () => setIsServiceBarOpen(window.innerWidth <= breakpoint);
    window.addEventListener("resize", handleResize);
    handleResize();

    return () => window.removeEventListener("resize", handleResize);
  }, [breakpoint]);

  return isServiceBarOpen;
};

export default useIsServiceBarOpenLocal;
