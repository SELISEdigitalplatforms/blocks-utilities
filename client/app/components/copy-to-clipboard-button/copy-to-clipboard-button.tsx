import { MouseEvent, ReactNode, useState } from "react";
import { Button } from "../ui-kits/button/button";
import { Check, Copy } from "lucide-react";

interface CopyToClipboardButtonProps {
  textToCopy: string;
  children: ReactNode;
  isHoverable?: boolean;
}

export const CopyToClipboardButton: React.FC<CopyToClipboardButtonProps> = ({
  textToCopy,
  children,
  isHoverable = false,
}) => {
  const [isCopying, setIsCopying] = useState(false);

  const copyToClipBoard = async (event: MouseEvent<HTMLButtonElement>) => {
    try {
      event.preventDefault();
      event.stopPropagation();

      if (isCopying) return;

      setIsCopying(true);

      if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(textToCopy);
      } else {
        const textArea = document.createElement("textarea");
        textArea.value = textToCopy;
        textArea.style.position = "fixed";
        textArea.style.left = "-999999px";
        textArea.style.top = "-999999px";
        document.body.appendChild(textArea);
        textArea.focus();
        textArea.select();
        document.execCommand("copy");
        document.body.removeChild(textArea);
      }
    } catch (err) {
      console.error("Failed to copy:", err);
      setIsCopying(false);
    } finally {
      setTimeout(() => {
        setIsCopying(false);
      }, 1000);
    }
  };

  return (
    <div className="group flex items-center gap-2">
      {children}
      <div
        className={`${
          isHoverable ? "opacity-0 group-hover:opacity-100" : "opacity-100"
        } relative flex min-w-[70px] items-center gap-1 transition-opacity duration-200`}
      >
        <Button
          variant="ghost"
          className="peer h-auto p-1 transition-colors hover:bg-gray-100"
          onClick={copyToClipBoard}
          type="button"
          disabled={isCopying}
        >
          {isCopying ? (
            <Check className="h-4 w-4 text-green-600" />
          ) : (
            <Copy className="h-4 w-4 text-gray-600 hover:text-gray-800" />
          )}
        </Button>
        <span className="absolute left-8 top-1/2 z-10 hidden -translate-y-1/2 whitespace-nowrap rounded bg-gray-900 px-2 py-1 text-xs text-white shadow-md peer-hover:block">
          {isCopying ? "Copied!" : "Copy"}
          <span className="absolute left-0 top-1/2 -translate-x-1 -translate-y-1/2 border-4 border-transparent border-r-gray-900"></span>
        </span>
      </div>
    </div>
  );
};
