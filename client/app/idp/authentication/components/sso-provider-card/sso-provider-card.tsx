import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { ISsoProviderConfigurationWithMeta } from "@blocks-idp/authentication/models/sso.model";
import { EllipsisVertical } from "lucide-react";
import { Link } from "react-router-dom";
import { SSoProviderStatusToggle } from "../sso-provider-status-toggle";
import { useMemo, useState } from "react";
import { useTheme } from "@/hooks/use-theme";

type SSOProviderCardProps = {
  configuration: ISsoProviderConfigurationWithMeta;
};

export const SSOProviderCardSkelton = () => {
  return (
    <Card>
      <CardHeader className="flex-row gap-2">
        <Skeleton className="aspect-square w-12" />
        <div className="flex-1">
          <Skeleton className="h-5 w-3/4" />
          <Skeleton className="mt-2 h-4 w-1/4" />
        </div>
      </CardHeader>
      <CardContent>
        <Skeleton className="h-5 w-full" />
        <Skeleton className="mt-2 h-5 w-2/4" />
      </CardContent>
    </Card>
  );
};
export const SSOProviderCard = ({ configuration }: SSOProviderCardProps) => {
  const [open, setOpen] = useState<boolean>(false);
  const { theme } = useTheme();
  const imageSrc = useMemo(() => {
    return theme === "light"
      ? configuration.imageSrc
      : configuration.imageSrcDark || configuration.imageSrc;
  }, [configuration, theme]);
  return (
    <>
      <Card>
        <CardHeader>
          <CardTitle className="flex justify-between">
            <div className="flex items-center gap-4">
              <div className="relative aspect-square h-8 w-8">
                <img src={imageSrc} alt="socical_icon"/>
              </div>
              <div>
                <div className="flex items-center justify-between gap-2 text-lg text-high-emphasis">
                  <h3>{configuration.label}</h3>
                  {configuration.isAvailable &&
                    configuration.itemId &&
                    !configuration.isDisabled && (
                      <Badge variant="success" className="h-fit text-xs">
                        Active
                      </Badge>
                    )}

                  {!configuration.isAvailable && (
                    <Badge variant="info" className="h-fit text-xs">
                      Coming soon
                    </Badge>
                  )}
                </div>
                <p className="text-sm font-normal text-low-emphasis">Social Connections</p>
              </div>
            </div>
            {configuration.isAvailable && (
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button variant="ghost" className="aspect-square w-10 rounded-full p-0">
                    <EllipsisVertical className="aspect-square w-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <Link
                    to={`/services/authentication/sso-configuration?provider=${configuration.provider}&id=${configuration.itemId || ""}`}
                  >
                    <DropdownMenuItem>Configure</DropdownMenuItem>
                  </Link>
                  {configuration.itemId && (
                    <DropdownMenuItem
                      onSelect={() => {
                        setOpen(true);
                      }}
                    >
                      {configuration.isDisabled ? "Enable" : "Disable"}
                    </DropdownMenuItem>
                  )}
                </DropdownMenuContent>
              </DropdownMenu>
            )}
          </CardTitle>
        </CardHeader>
        <CardContent>
          <div className="mt-4 text-medium-emphasis">{configuration.description}</div>
        </CardContent>
      </Card>
      <SSoProviderStatusToggle open={open} setOpen={setOpen} configuration={configuration} />
    </>
  );
};
