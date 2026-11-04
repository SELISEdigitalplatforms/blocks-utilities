import React from "react";
import { useT } from "@seliseblocks/genesis-os";

const SpinnerLoader = () => {
  const { t } = useT("utilities.shared");
  return (
    <div
      className="inline-block h-8 w-8 animate-spin rounded-full border-4 border-solid border-primary border-r-transparent"
      role="status"
    >
      <span className="sr-only">{t("common:LOADING")}</span>
    </div>
  );
};

export default SpinnerLoader;
