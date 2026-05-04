import { useState, useEffect } from "react";

const useIsServiceBarOpenComm = (breakpoint = 1038) => {
  const [isServiceBarOpen, setIsServiceBarOpen] = useState(false);
  
  useEffect(() => {
    const handleResize = () => setIsServiceBarOpen(window.innerWidth <= breakpoint);
    window.addEventListener("resize", handleResize);
    handleResize();

    return () => window.removeEventListener("resize", handleResize);
  }, [breakpoint]);

  return isServiceBarOpen;
};

export default useIsServiceBarOpenComm;
