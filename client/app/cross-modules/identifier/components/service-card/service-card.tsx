import { useState } from "react";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useNavigate } from "react-router-dom";
import { RegisteredService } from "@blocks-identifier/models/service.model";
import { Button } from "@/components/ui-kits/button/button";
import {
  Activity,
  BookOpen,
  Braces,
  Copy,
  EllipsisVertical,
  FileText,
} from "lucide-react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { MaskedText } from "@/components/masked-text";
import { useCopyToClipboard } from "@/hooks/use-copy-to-clipboard";
import { showSuccessToast } from "@/hooks/use-toast";
import { Badge } from "@/components/ui-kits/badge/badge";
import { AccordionTrigger } from "@/components/ui-kits/accordion/accordion";
import { AccordionContent } from "@radix-ui/react-accordion";

const ServiceCardCopiedItem = ({ label, value }: { label: string; value: string }) => {
  const { copy } = useCopyToClipboard();

  return (
    <div className="text-sm">
      <div className="text-low-emphasis">{label}</div>
      <div className="flex items-center gap-4">
        <div className="text-base text-high-emphasis">
          <MaskedText text={value} showFirstN={3} length={25} showLastN={3} />
        </div>
        <Button
          variant="ghost"
          size="sm"
          className="h-5 w-5 p-0 text-gray-400 hover:text-gray-600"
          onClick={() => copy(value, () => showSuccessToast({ description: `${label} copied` }))}
        >
          <Copy className="h-4 w-4" />
        </Button>
      </div>
    </div>
  );
};

const LinkButton = ({
  onClick,
  children,
}: {
  onClick: () => void;
  children: React.ReactNode;
}) => (
  <button
    type="button"
    onClick={onClick}
    className="inline-flex items-center justify-center whitespace-nowrap rounded-sm border border-input bg-background p-2 py-1.5 text-sm font-medium ring-offset-background transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
  >
    {children}
  </button>
);

export const ServiceCard = ({ service }: { service: RegisteredService }) => {
  const [showAllTags, setShowAllTags] = useState(false);
  const navigate = useNavigate();

  const swaggerUrl = `${getRuntimeEnv("BLOCKS_API_BASE_URL")}/identifier/v1/swagger/index.html`;
  const docsUrl = "https://docs.seliseblocks.com/";

  return (
    <>
      <AccordionTrigger className="overflow-hidden p-4 hover:no-underline">
        <div className="mr-4 flex min-w-0 flex-1 items-center justify-between gap-4">
          <div className="flex min-w-0 flex-1 items-center gap-4">
            <h3 className="truncate text-lg font-semibold text-gray-900 dark:text-white">
              {service.name}
            </h3>
            <Badge variant="info" className="w-fit shrink-0 capitalize">
              {service.serviceType || "backend"}
            </Badge>
          </div>

          <div className="flex shrink-0 items-center gap-2">
            <LinkButton
              onClick={() =>
                navigate(
                  `/services/lmt/logs/${encodeURIComponent(service.serviceId)}?name=${encodeURIComponent(service.name)}`,
                )
              }
            >
              <FileText className="h-4 w-4" />
              <span className="sr-only sm:not-sr-only sm:ml-2">Logs</span>
            </LinkButton>

            <LinkButton
              onClick={() =>
                navigate(`/services/lmt?tab=tracing&services=${service.serviceId}`)
              }
            >
              <Activity className="h-4 w-4" />
              <span className="sr-only sm:not-sr-only sm:ml-2">Traces</span>
            </LinkButton>

            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <div className="flex h-8 w-8 cursor-pointer items-center justify-center rounded-sm p-0 hover:bg-accent hover:text-accent-foreground">
                  <EllipsisVertical className="h-4 w-4" />
                </div>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem
                  onClick={() => window.open(swaggerUrl, "_blank", "noopener,noreferrer")}
                >
                  <Braces className="mr-2 aspect-square w-4" />
                  Swagger
                </DropdownMenuItem>
                <DropdownMenuItem
                  onClick={() => window.open(docsUrl, "_blank", "noopener,noreferrer")}
                >
                  <BookOpen className="mr-2 aspect-square w-4" />
                  Docs
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </div>
      </AccordionTrigger>

      <AccordionContent className="p-4">
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
          <ServiceCardCopiedItem label="Service ID" value={service.serviceId} />
          {service.serviceBusConnectionString && service.serviceType !== "frontend" && (
            <ServiceCardCopiedItem
              label="Connection String"
              value={service.serviceBusConnectionString}
            />
          )}
          <ServiceCardCopiedItem label="X-Blocks-Key" value={service.tenantId} />
        </div>

        {service.description && (
          <div className="mt-3 text-sm">
            <h3 className="text-low-emphasis">Description</h3>
            <p className="break-words text-high-emphasis">{service.description}</p>
          </div>
        )}

        {service.tags?.length > 0 && (
          <div className="mt-3 flex flex-wrap gap-1">
            {(showAllTags ? service.tags : service.tags.slice(0, 4)).map((tag, index) => (
              <Badge key={`${tag}-${index}`} variant="secondary" className="text-xs">
                {tag}
              </Badge>
            ))}
            {!showAllTags && service.tags.length > 4 && (
              <Badge
                variant="secondary"
                className="cursor-pointer text-xs"
                onClick={() => setShowAllTags(true)}
              >
                +{service.tags.length - 4}
              </Badge>
            )}
          </div>
        )}
      </AccordionContent>
    </>
  );
};
