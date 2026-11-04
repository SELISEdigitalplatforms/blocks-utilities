import { CreditCard, Trash2 } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import type { StoredPaymentMethod } from "../models/stored-payment-method.model";
import { tStatic, useT } from "@seliseblocks/genesis-os";

const UTILITIES_PAYMENT_MODULE = "utilities.payment";

interface StoredPaymentMethodTableProps {
  methods: StoredPaymentMethod[];
  removingPaymentMethodId: string | null;
  onRemove: (method: StoredPaymentMethod) => void;
}

const displayValue = (value: string | null) =>
  value?.trim() || tStatic(UTILITIES_PAYMENT_MODULE, "STORED_PAYMENT_METHOD_TABLE.NOT_AVAILABLE");

const formatBrand = (brand: string | null) => {
  if (!brand) {
    return tStatic(UTILITIES_PAYMENT_MODULE, "STORED_PAYMENT_METHOD_TABLE.PAYMENT_METHOD");
  }

  return brand
    .replace(/credit$/i, "")
    .replace(/[_-]+/g, " ")
    .replace(/\b\w/g, (letter) => letter.toUpperCase())
    .trim();
};

const formatMaskedNumber = (lastFour: string | null) =>
  lastFour ? `•••• •••• •••• ${lastFour}` : tStatic(UTILITIES_PAYMENT_MODULE, "STORED_PAYMENT_METHOD_TABLE.MASKED_DETAILS_UNAVAILABLE");

const formatExpiry = (
  month: string | null,
  year: string | null,
) => (month && year ? `${month.padStart(2, "0")}/${year}` : "—");

export const StoredPaymentMethodTable = ({
  methods,
  removingPaymentMethodId,
  onRemove,
}: StoredPaymentMethodTableProps) => {
  const { t } = useT(UTILITIES_PAYMENT_MODULE);
  return (
  <>
    <div className="hidden overflow-hidden rounded-xl border md:block">
      <Table>
        <TableHeader className="bg-muted/40">
          <TableRow>
            <TableHead>{t("STORED_PAYMENT_METHOD_TABLE.PAYMENT_METHOD")}</TableHead>
            <TableHead>{t("STORED_PAYMENT_METHOD_TABLE.MASKED_DETAILS")}</TableHead>
            <TableHead>{t("common:TYPE")}</TableHead>
            <TableHead>{t("STORED_PAYMENT_METHOD_TABLE.EXPIRY")}</TableHead>
            <TableHead>{t("STORED_PAYMENT_METHOD_TABLE.FUNDING_SOURCE")}</TableHead>
            <TableHead>{t("STORED_PAYMENT_METHOD_TABLE.ISSUER_COUNTRY")}</TableHead>
            <TableHead>{t("common:STATUS")}</TableHead>
            <TableHead className="text-right">{t("STORED_PAYMENT_METHOD_TABLE.ACTION")}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {methods.map((method) => (
            <TableRow key={method.paymentMethodId}>
              <TableCell>
                <div className="flex items-center gap-2.5">
                  <span className="rounded-lg bg-blocks-primary-shades-200 p-2 text-blocks-primary-600">
                    <CreditCard className="h-4 w-4" />
                  </span>
                  <div>
                    <p className="font-medium">
                      {formatBrand(method.brand)}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {method.paymentMethodId.slice(0, 8)}
                    </p>
                  </div>
                </div>
              </TableCell>
              <TableCell className="font-mono text-xs">
                {formatMaskedNumber(method.lastFour)}
              </TableCell>
              <TableCell>{displayValue(method.type)}</TableCell>
              <TableCell>
                {formatExpiry(method.expiryMonth, method.expiryYear)}
              </TableCell>
              <TableCell>
                {displayValue(method.fundingSource)}
              </TableCell>
              <TableCell>
                {displayValue(method.issuerCountry)}
              </TableCell>
              <TableCell>
                <Badge
                  variant="success"
                  className="w-fit rounded-full px-2.5 py-1"
                >
                  {method.status}
                </Badge>
              </TableCell>
              <TableCell className="text-right">
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="text-destructive hover:bg-destructive/10 hover:text-destructive"
                  disabled={
                    removingPaymentMethodId ===
                    method.paymentMethodId
                  }
                  aria-label={t("STORED_PAYMENT_METHOD_TABLE.REMOVE_ENDING_IN", { brand: formatBrand(method.brand), lastFour: method.lastFour ?? t("STORED_PAYMENT_METHOD_TABLE.UNKNOWN_DIGITS") })}
                  onClick={() => onRemove(method)}
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>

    <div className="grid gap-3 md:hidden">
      {methods.map((method) => (
        <article
          key={method.paymentMethodId}
          className="rounded-xl border bg-card p-4 shadow-sm"
        >
          <div className="flex items-start justify-between gap-3">
            <div className="flex min-w-0 items-center gap-3">
              <span className="rounded-lg bg-blocks-primary-shades-200 p-2.5 text-blocks-primary-600">
                <CreditCard className="h-5 w-5" />
              </span>
              <div className="min-w-0">
                <p className="truncate font-semibold">
                  {formatBrand(method.brand)}
                </p>
                <p className="mt-1 font-mono text-xs text-muted-foreground">
                  {formatMaskedNumber(method.lastFour)}
                </p>
              </div>
            </div>
            <Badge
              variant="success"
              className="w-fit rounded-full px-2.5 py-1"
            >
              {method.status}
            </Badge>
          </div>

          <dl className="mt-4 grid grid-cols-2 gap-3 border-t pt-4 text-sm">
            <div>
              <dt className="text-xs text-muted-foreground">{t("STORED_PAYMENT_METHOD_TABLE.EXPIRY")}</dt>
              <dd className="mt-1 font-medium">
                {formatExpiry(method.expiryMonth, method.expiryYear)}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">{t("common:TYPE")}</dt>
              <dd className="mt-1 font-medium">
                {displayValue(method.type)}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">
                {t("STORED_PAYMENT_METHOD_TABLE.FUNDING_SOURCE")}
              </dt>
              <dd className="mt-1 font-medium">
                {displayValue(method.fundingSource)}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">
                {t("STORED_PAYMENT_METHOD_TABLE.ISSUER_COUNTRY")}
              </dt>
              <dd className="mt-1 font-medium">
                {displayValue(method.issuerCountry)}
              </dd>
            </div>
          </dl>

          <Button
            type="button"
            variant="destructive-outline"
            size="sm"
            className="mt-4 w-full"
            disabled={
              removingPaymentMethodId === method.paymentMethodId
            }
            onClick={() => onRemove(method)}
          >
            <Trash2 className="mr-2 h-4 w-4" />
            {t("STORED_PAYMENT_METHOD_TABLE.REMOVE_PAYMENT_METHOD")}
          </Button>
        </article>
      ))}
    </div>
  </>
);
};
