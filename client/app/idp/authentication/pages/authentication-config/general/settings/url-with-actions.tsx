import { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { Check, Copy, Download } from "lucide-react";
import { getApiUrl } from "@/lib/get-api-path";
import { useProjectStore } from "@/store/useProjectStore";

interface UrlWithActionsProps {
  url: string;
}

export const UrlWithActions = ({ url }: UrlWithActionsProps) => {
  const [isCopying, setIsCopying] = useState(false);
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const jwksUrl = `${getApiUrl("idp/v1", ".well-known/jwks.json")}?X-Blocks-Key=${tenantId}`;

  const handleCopy = async (event: React.MouseEvent<HTMLButtonElement>) => {
    try {
      event.preventDefault();
      event.stopPropagation();

      if (isCopying) return;

      setIsCopying(true);

      if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(jwksUrl);
      } else {
        const textArea = document.createElement("textarea");
        textArea.value = jwksUrl;
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

  const handleDownload = async () => {
    try {
      const response = await fetch(url);
      const blob = await response.blob();
      const downloadUrl = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = downloadUrl;
      link.download = url.split("/").pop() || "certificate.pem";
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      URL.revokeObjectURL(downloadUrl);
    } catch (err) {
      console.error("Failed to download:", err);
    }
  };

  return (
    <div className="group flex min-w-0 items-center gap-1">
      <span className="text-base font-normal text-high-emphasis underline" title={jwksUrl}>
        certificate
      </span>
      <div className="flex flex-shrink-0 items-center gap-1 opacity-0 transition-opacity duration-200 group-hover:opacity-100">
        <Button
          variant="ghost"
          className="h-auto p-1 transition-colors hover:bg-gray-100"
          onClick={handleCopy}
          type="button"
          title={isCopying ? "Copied!" : "Copy URL"}
        >
          {isCopying ? (
            <Check className="h-4 w-4 text-green-600" />
          ) : (
            <Copy className="h-4 w-4 text-gray-600 hover:text-gray-800" />
          )}
        </Button>
        <Button
          variant="ghost"
          className="h-auto p-1 transition-colors hover:bg-gray-100"
          onClick={handleDownload}
          type="button"
          title="Download certificate"
        >
          <Download className="h-4 w-4 text-gray-600 hover:text-gray-800" />
        </Button>
      </div>
    </div>
  );
};
