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
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore, useT } from "@seliseblocks/genesis-os";
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

const ORGANIZATION_PAGE_SIZE = 200;

export const PaymentProviderListPage = () => {
  const { t } = useT("utilities.payment");
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

  const tenantId = useProjectStore()?.selectedProject?.tenantId ?? "";
  const { data: organizationsData } = useGetOrganizations({
    projectKey: tenantId,
    page: 0,
    pageSize: ORGANIZATION_PAGE_SIZE,
  });

  // Falls back to the raw identifier rather than hiding the row's organization: an id the
  // reader does not recognise is still more use than a blank cell.
  const organizationName = useMemo(() => {
    const names = new Map(
      (organizationsData?.organizations ?? []).map((organization) => [
        organization.itemId,
        organization.name,
      ]),
    );

    return (organizationId: string | null) =>
      organizationId
        ? (names.get(organizationId) ?? organizationId)
        : t("PAYMENT_PROVIDER_LIST_PAGE.TENANT_LEVEL");
  }, [organizationsData, t]);

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
        title={t("PAYMENT_PROVIDER_LIST_PAGE.PAYMENT_PROVIDERS")}
        description={t("PAYMENT_PROVIDER_LIST_PAGE.REGISTER_AND_MANAGE_TENANT_SCOPED")}
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
              {t("common:REFRESH")}
            </Button>
            <Button asChild>
              <Link to={`${basePath}/create`}>
                <Plus className="mr-2 h-4 w-4" />
                {t("PAYMENT_PROVIDER_LIST_PAGE.CREATE_PROVIDER")}
              </Link>
            </Button>
          </>
        }
      />

      <Card className="rounded-xl p-0">
        <div className="flex flex-col gap-3 border-b p-4 sm:flex-row sm:items-center sm:justify-between sm:p-5">
          <div>
            <h2 className="font-semibold">{t("PAYMENT_PROVIDER_LIST_PAGE.REGISTERED_PROVIDERS")}</h2>
            <p className="text-xs text-muted-foreground">
              {t("PAYMENT_PROVIDER_LIST_PAGE.CREDENTIALS_ARE_NEVER_RETURNED_BY")}
            </p>
          </div>

          <div className="flex w-full flex-col gap-2 sm:w-auto sm:flex-row">
            <div className="relative min-w-0 sm:w-64">
              <Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
              <Input
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder={t("PAYMENT_PROVIDER_LIST_PAGE.SEARCH_PROVIDER_OR_MERCHANT")}
                className="pl-9"
                aria-label={t("PAYMENT_PROVIDER_LIST_PAGE.SEARCH_PAYMENT_PROVIDERS")}
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
                <SelectItem value="all">{t("PAYMENT_PROVIDER_LIST_PAGE.ALL_STATUSES")}</SelectItem>
                <SelectItem value="enabled">{t("common:ENABLED")}</SelectItem>
                <SelectItem value="disabled">{t("common:DISABLED")}</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>

        {isLoading ? (
          <div className="space-y-3 p-5" aria-label={t("PAYMENT_PROVIDER_LIST_PAGE.LOADING_PROVIDERS")}>
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
              {t("PAYMENT_PROVIDER_LIST_PAGE.PROVIDERS_COULD_NOT_BE_LOADED")}
            </h3>
            <p className="mt-1 max-w-md text-sm text-muted-foreground">
              {error instanceof Error
                ? error.message
                : t("PAYMENT_PROVIDER_LIST_PAGE.TRY_LOADING_THE_PROVIDER_LIST")}
            </p>
            <Button
              className="mt-5"
              variant="outline"
              onClick={() => refetch()}
            >
              {t("PAYMENT_PROVIDER_LIST_PAGE.TRY_AGAIN")}
            </Button>
          </div>
        ) : filteredProviders.length === 0 ? (
          <div className="flex min-h-72 flex-col items-center justify-center px-5 text-center">
            <span className="rounded-full bg-muted p-4 text-muted-foreground">
              <Settings2 className="h-7 w-7" />
            </span>
            <h3 className="mt-4 text-lg font-semibold">
              {providers?.length
                ? t("PAYMENT_PROVIDER_LIST_PAGE.NO_PROVIDERS_MATCH_THESE_FILTERS")
                : t("PAYMENT_PROVIDER_LIST_PAGE.NO_PAYMENT_PROVIDER_REGISTERED")}
            </h3>
            <p className="mt-1 max-w-md text-sm text-muted-foreground">
              {providers?.length
                ? t("PAYMENT_PROVIDER_LIST_PAGE.CHANGE_THE_SEARCH_OR_STATUS")
                : t("PAYMENT_PROVIDER_LIST_PAGE.REGISTER_A_PROVIDER_BEFORE")}
            </p>
            {!providers?.length && (
              <Button asChild className="mt-5">
                <Link to={`${basePath}/create`}>
                  <Plus className="mr-2 h-4 w-4" />
                  {t("PAYMENT_PROVIDER_LIST_PAGE.CREATE_PROVIDER")}
                </Link>
              </Button>
            )}
          </div>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t("PAYMENT_PROVIDER_LIST_PAGE.PROVIDER")}</TableHead>
                <TableHead>{t("PAYMENT_PROVIDER_LIST_PAGE.MERCHANT")}</TableHead>
                <TableHead>{t("PAYMENT_PROVIDER_LIST_PAGE.ORGANIZATION")}</TableHead>
                <TableHead>{t("PAYMENT_PROVIDER_LIST_PAGE.COUNTRY")}</TableHead>
                <TableHead>{t("PAYMENT_PROVIDER_LIST_PAGE.CAPTURE")}</TableHead>
                <TableHead>{t("common:STATUS")}</TableHead>
                <TableHead>{t("PAYMENT_PROVIDER_LIST_PAGE.VERSION")}</TableHead>
                <TableHead className="text-right">{t("common:ACTIONS")}</TableHead>
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
                  <TableCell>
                    {/* The uniqueness index allows the same provider and merchant in two
                        organizations, so without this the rows are indistinguishable. */}
                    <span
                      className="block max-w-56 truncate"
                      title={provider.organizationId ?? t("PAYMENT_PROVIDER_LIST_PAGE.TENANT_LEVEL")}
                    >
                      {organizationName(provider.organizationId)}
                    </span>
                  </TableCell>
                  <TableCell>{provider.countryCode || "—"}</TableCell>
                  <TableCell>
                    {provider.manualCapture ? t("PAYMENT_PROVIDER_LIST_PAGE.MANUAL") : t("PAYMENT_PROVIDER_LIST_PAGE.AUTOMATIC")}
                  </TableCell>
                  <TableCell>
                    <Badge
                      variant={provider.isEnabled ? "success" : "outline"}
                      className="w-fit"
                    >
                      {provider.isEnabled ? t("common:ENABLED") : t("common:DISABLED")}
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
                          {t("common:EDIT")}
                        </Link>
                      </Button>
                      <Button asChild variant="outline" size="sm">
                        <Link
                          to={`${basePath}/${encodeURIComponent(provider.paymentProviderId)}/rotate`}
                        >
                          <KeyRound className="mr-2 h-4 w-4" />
                          {t("PAYMENT_PROVIDER_LIST_PAGE.ROTATE")}
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
