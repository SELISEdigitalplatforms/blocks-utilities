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

interface StoredPaymentMethodTableProps {
  methods: StoredPaymentMethod[];
  removingPaymentMethodId: string | null;
  onRemove: (method: StoredPaymentMethod) => void;
}

const displayValue = (value: string | null) =>
  value?.trim() || "Not available";

const formatBrand = (brand: string | null) => {
  if (!brand) {
    return "Payment method";
  }

  return brand
    .replace(/credit$/i, "")
    .replace(/[_-]+/g, " ")
    .replace(/\b\w/g, (letter) => letter.toUpperCase())
    .trim();
};

const formatMaskedNumber = (lastFour: string | null) =>
  lastFour ? `•••• •••• •••• ${lastFour}` : "Masked details unavailable";

const formatExpiry = (
  month: string | null,
  year: string | null,
) => (month && year ? `${month.padStart(2, "0")}/${year}` : "—");

export const StoredPaymentMethodTable = ({
  methods,
  removingPaymentMethodId,
  onRemove,
}: StoredPaymentMethodTableProps) => (
  <>
    <div className="hidden overflow-hidden rounded-xl border md:block">
      <Table>
        <TableHeader className="bg-muted/40">
          <TableRow>
            <TableHead>Payment method</TableHead>
            <TableHead>Masked details</TableHead>
            <TableHead>Type</TableHead>
            <TableHead>Expiry</TableHead>
            <TableHead>Funding source</TableHead>
            <TableHead>Issuer country</TableHead>
            <TableHead>Status</TableHead>
            <TableHead className="text-right">Action</TableHead>
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
                  aria-label={`Remove ${formatBrand(method.brand)} ending in ${method.lastFour ?? "unknown digits"}`}
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
              <dt className="text-xs text-muted-foreground">Expiry</dt>
              <dd className="mt-1 font-medium">
                {formatExpiry(method.expiryMonth, method.expiryYear)}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">Type</dt>
              <dd className="mt-1 font-medium">
                {displayValue(method.type)}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">
                Funding source
              </dt>
              <dd className="mt-1 font-medium">
                {displayValue(method.fundingSource)}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">
                Issuer country
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
            Remove payment method
          </Button>
        </article>
      ))}
    </div>
  </>
);
