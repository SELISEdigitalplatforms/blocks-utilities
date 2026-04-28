import React from "react";

interface MaskedTextProps {
  text: string;
  length?: number;
  showFirstN?: number;
  showLastN?: number;
  char?: string;
}

export const MaskedText: React.FC<MaskedTextProps> = ({
  text,
  length,
  showFirstN = 0,
  showLastN = 0,
  char = "*",
}) => {
  const actualLength = length ?? text?.length ?? 0;

  const firstVisible = showFirstN > 0 ? text.slice(0, showFirstN) : "";
  const lastVisible = showLastN > 0 ? text.slice(-showLastN) : "";

  const maskedCount = Math.max(actualLength - showFirstN - showLastN, 0);

  return (
    <div className="flex min-w-0 items-center overflow-hidden">
      {firstVisible && <span className="shrink-0">{firstVisible}</span>}
      <span className="h-4 min-w-0 flex-1 overflow-hidden">{char.repeat(maskedCount)}</span>
      {lastVisible && <span className="shrink-0">{lastVisible}</span>}
    </div>
  );
};
