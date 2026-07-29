import { useMemo, useState } from "react";
import {
  AlertCircle,
  KeyRound,
  Pencil,
  Plus,
  RefreshCw,
  Search,
  Settings2,
} from "lucide-react";
import { Link, useParams } from "react-router";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { PaymentProviderPageHeader } from "../components/payment-provider-page-header";
import { usePaymentProviders } from "../hooks/use-payment-providers";
import { providerDisplayName } from "../schemas/payment-provider.schema";

type StatusFilter = "all" | "enabled" | "disabled";

export const PaymentProviderListPage = () => {
  const { itemId } = useParams();
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<StatusFilter>("all");
  const {
    data: providers,
    error,
    isError,
    isFetching,
    isLoading,
    refetch,
  } = usePaymentProviders();

  const basePath = `/app/${itemId ?? ""}/payment/providers`;
  const filteredProviders = useMemo(() => {
    const term = search.trim().toLowerCase();

    return (providers ?? []).filter((provider) => {
      const matchesStatus =
        status === "all" ||
        (status === "enabled" && provider.isEnabled) ||
        (status === "disabled" && !provider.isEnabled);
      const matchesSearch =
        !term ||
        provider.providerName.toLowerCase().includes(term) ||
        provider.merchantId.toLowerCase().includes(term) ||
        (provider.countryCode ?? "").toLowerCase().includes(term);

      return matchesStatus && matchesSearch;
    });
  }, [providers, search, status]);

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <PaymentProviderPageHeader
        title="Payment providers"
        description="Register and manage tenant-scoped provider configuration and credentials."
        actions={
          <>
            <Button
              variant="outline"
              className="bg-background/80"
              onClick={() => refetch()}
              disabled={isFetching}
            >
              <RefreshCw
                className={`mr-2 h-4 w-4 ${isFetching ? "animate-spin" : ""}`}
              />
              Refresh
            </Button>
            <Button asChild>
              <Link to={`${basePath}/create`}>
                <Plus className="mr-2 h-4 w-4" />
                Create provider
              </Link>
            </Button>
          </>
        }
      />

      <Card className="rounded-xl p-0">
        <div className="flex flex-col gap-3 border-b p-4 sm:flex-row sm:items-center sm:justify-between sm:p-5">
          <div>
            <h2 className="font-semibold">Registered providers</h2>
            <p className="text-xs text-muted-foreground">
              Credentials are never returned by this endpoint.
            </p>
          </div>

          <div className="flex w-full flex-col gap-2 sm:w-auto sm:flex-row">
            <div className="relative min-w-0 sm:w-64">
              <Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
              <Input
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Search provider or merchant"
                className="pl-9"
                aria-label="Search payment providers"
              />
            </div>
            <Select
              value={status}
              onValueChange={(value) =>
                setStatus(value as StatusFilter)
              }
            >
              <SelectTrigger className="sm:w-36">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All statuses</SelectItem>
                <SelectItem value="enabled">Enabled</SelectItem>
                <SelectItem value="disabled">Disabled</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>

        {isLoading ? (
          <div className="space-y-3 p-5" aria-label="Loading providers">
            {Array.from({ length: 4 }, (_, index) => (
              <Skeleton key={index} className="h-14 w-full" />
            ))}
          </div>
        ) : isError ? (
          <div className="flex min-h-72 flex-col items-center justify-center px-5 text-center">
            <span className="rounded-full bg-destructive/10 p-4 text-destructive">
              <AlertCircle className="h-7 w-7" />
            </span>
            <h3 className="mt-4 text-lg font-semibold">
              Providers could not be loaded
            </h3>
            <p className="mt-1 max-w-md text-sm text-muted-foreground">
              {error instanceof Error
                ? error.message
                : "Try loading the provider list again."}
            </p>
            <Button
              className="mt-5"
              variant="outline"
              onClick={() => refetch()}
            >
              Try again
            </Button>
          </div>
        ) : filteredProviders.length === 0 ? (
          <div className="flex min-h-72 flex-col items-center justify-center px-5 text-center">
            <span className="rounded-full bg-muted p-4 text-muted-foreground">
              <Settings2 className="h-7 w-7" />
            </span>
            <h3 className="mt-4 text-lg font-semibold">
              {providers?.length
                ? "No providers match these filters"
                : "No payment provider registered"}
            </h3>
            <p className="mt-1 max-w-md text-sm text-muted-foreground">
              {providers?.length
                ? "Change the search or status filter."
                : "Register a provider before creating payment sessions."}
            </p>
            {!providers?.length && (
              <Button asChild className="mt-5">
                <Link to={`${basePath}/create`}>
                  <Plus className="mr-2 h-4 w-4" />
                  Create provider
                </Link>
              </Button>
            )}
          </div>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Provider</TableHead>
                <TableHead>Merchant</TableHead>
                <TableHead>Country</TableHead>
                <TableHead>Capture</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Version</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {filteredProviders.map((provider) => (
                <TableRow key={provider.paymentProviderId}>
                  <TableCell>
                    <div className="font-medium">
                      {providerDisplayName(provider.providerName)}
                    </div>
                    <div className="text-xs text-muted-foreground">
                      {provider.providerName}
                    </div>
                  </TableCell>
                  <TableCell>
                    <span
                      className="block max-w-56 truncate"
                      title={provider.merchantId}
                    >
                      {provider.merchantId}
                    </span>
                  </TableCell>
                  <TableCell>{provider.countryCode || "—"}</TableCell>
                  <TableCell>
                    {provider.manualCapture ? "Manual" : "Automatic"}
                  </TableCell>
                  <TableCell>
                    <Badge
                      variant={provider.isEnabled ? "success" : "outline"}
                      className="w-fit"
                    >
                      {provider.isEnabled ? "Enabled" : "Disabled"}
                    </Badge>
                  </TableCell>
                  <TableCell className="tabular-nums">
                    {provider.version}
                  </TableCell>
                  <TableCell>
                    <div className="flex justify-end gap-2">
                      <Button asChild variant="outline" size="sm">
                        <Link
                          to={`${basePath}/${encodeURIComponent(provider.paymentProviderId)}/edit`}
                        >
                          <Pencil className="mr-2 h-4 w-4" />
                          Edit
                        </Link>
                      </Button>
                      <Button asChild variant="outline" size="sm">
                        <Link
                          to={`${basePath}/${encodeURIComponent(provider.paymentProviderId)}/rotate`}
                        >
                          <KeyRound className="mr-2 h-4 w-4" />
                          Rotate
                        </Link>
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Card>
    </main>
  );
};
