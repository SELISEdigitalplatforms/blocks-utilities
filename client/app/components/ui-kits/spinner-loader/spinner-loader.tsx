import React from "react";

const SpinnerLoader = () => {
  return (
    <div
      className="inline-block h-8 w-8 animate-spin rounded-full border-4 border-solid border-primary border-r-transparent"
      role="status"
    >
      <span className="sr-only">Loading...</span>
    </div>
  );
};

export default SpinnerLoader;
