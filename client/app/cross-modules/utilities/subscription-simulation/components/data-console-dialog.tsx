import { AlertCircle, Loader2 } from "lucide-react";
import { useState } from "react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { toast } from "@/hooks/use-toast";
import { useDataConsolePolicy } from "../hooks/use-data-console-policy";
import { useFindData } from "../hooks/use-find-data";
import { useUpdateDataFields } from "../hooks/use-update-data-fields";
import type {
  SubscriptionSimulationDataMutationResponse,
  SubscriptionSimulationDataQueryResponse,
} from "../models/subscription-simulation-harness.model";
import { useT } from "@seliseblocks/genesis-os";

const prettyPrint = (json: string): string => {
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
};

const FindTab = ({
  subscriptionId,
  organizationId,
  readableCollections,
}: {
  subscriptionId: string;
  organizationId: string | undefined;
  readableCollections: string[];
}) => {
  const { t } = useT("utilities.subscription-simulation");
  const { mutateAsync, isPending } = useFindData();

  const [collection, setCollection] = useState(readableCollections[0] ?? "");
  const [limit, setLimit] = useState("20");
  const [formError, setFormError] = useState<string | null>(null);
  const [result, setResult] = useState<SubscriptionSimulationDataQueryResponse | null>(null);

  const submit = async () => {
    setFormError(null);

    const parsedLimit = Number(limit);
    if (!collection) {
      setFormError(t("DATA_CONSOLE_DIALOG.CHOOSE_A_COLLECTION_2"));
      return;
    }
    if (!Number.isInteger(parsedLimit) || parsedLimit < 1 || parsedLimit > 100) {
      setFormError(t("DATA_CONSOLE_DIALOG.LIMIT_MUST_BE_A_WHOLE"));
      return;
    }

    try {
      const response = await mutateAsync({
        subscriptionId,
        logicalCollection: collection,
        request: { organizationId, subscriptionId, limit: parsedLimit },
      });

      setResult(response);
    } catch (error) {
      setResult(null);
      setFormError(error instanceof Error ? error.message : t("DATA_CONSOLE_DIALOG.THE_DATA_COULD_NOT_BE"));
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end gap-3">
        <div className="min-w-48 flex-1 space-y-1.5">
          <Label htmlFor="data-console-find-collection">{t("DATA_CONSOLE_DIALOG.COLLECTION")}</Label>
          <Select value={collection} onValueChange={setCollection}>
            <SelectTrigger id="data-console-find-collection">
              <SelectValue placeholder={t("DATA_CONSOLE_DIALOG.CHOOSE_A_COLLECTION")} />
            </SelectTrigger>
            <SelectContent>
              {readableCollections.map((name) => (
                <SelectItem key={name} value={name}>
                  {name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="w-28 space-y-1.5">
          <Label htmlFor="data-console-find-limit">{t("DATA_CONSOLE_DIALOG.LIMIT")}</Label>
          <Input
            id="data-console-find-limit"
            type="number"
            min={1}
            max={100}
            value={limit}
            onChange={(event) => setLimit(event.target.value)}
          />
        </div>

        <Button onClick={submit} disabled={isPending || !collection}>
          {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
          {t("DATA_CONSOLE_DIALOG.FIND")}
        </Button>
      </div>

      {formError && <p className="text-sm text-destructive">{formError}</p>}

      {result && (
        <div className="space-y-2">
          <p className="text-xs text-muted-foreground">
            {t("DATA_CONSOLE_DIALOG.DOCUMENTS_FROM", { count: result.count, collection: result.collection })}</p>
          {result.documents.length === 0 ? (
            <p className="py-6 text-center text-sm text-muted-foreground">
              {t("DATA_CONSOLE_DIALOG.NO_MATCHING_DOCUMENTS")}
            </p>
          ) : (
            <div className="max-h-96 space-y-2 overflow-y-auto">
              {result.documents.map((document, index) => (
                // eslint-disable-next-line react/no-array-index-key -- documents carry no stable id of their own once redacted
                <pre key={index} className="overflow-x-auto rounded-md bg-muted p-3 text-xs">
                  {prettyPrint(document)}
                </pre>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
};

const UpdateTab = ({
  subscriptionId,
  organizationId,
  updatableCollections,
}: {
  subscriptionId: string;
  organizationId: string | undefined;
  updatableCollections: { logicalName: string; updatableFields: string[] }[];
}) => {
  const { t } = useT("utilities.subscription-simulation");
  const { mutateAsync, isPending } = useUpdateDataFields();

  const [collectionName, setCollectionName] = useState(updatableCollections[0]?.logicalName ?? "");
  const [values, setValues] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [result, setResult] = useState<SubscriptionSimulationDataMutationResponse | null>(null);

  const collection = updatableCollections.find((entry) => entry.logicalName === collectionName);

  const selectCollection = (name: string) => {
    setCollectionName(name);
    setValues({});
    setFormError(null);
    setResult(null);
  };

  const submit = async () => {
    setFormError(null);

    const fields = Object.fromEntries(
      Object.entries(values).filter(([, value]) => value.trim().length > 0),
    );

    if (!collection) {
      setFormError(t("DATA_CONSOLE_DIALOG.CHOOSE_A_COLLECTION_2"));
      return;
    }
    if (Object.keys(fields).length === 0) {
      setFormError(t("DATA_CONSOLE_DIALOG.SET_AT_LEAST_ONE_FIELD"));
      return;
    }

    try {
      const response = await mutateAsync({
        subscriptionId,
        logicalCollection: collection.logicalName,
        request: { organizationId, subscriptionId, fields },
      });

      setResult(response);

      toast({
        variant: "success",
        title: response.modified ? t("DATA_CONSOLE_DIALOG.FIELD_UPDATED") : t("DATA_CONSOLE_DIALOG.NOTHING_MATCHED"),
        description: response.modified
          ? t("DATA_CONSOLE_DIALOG.SET_ON", { fields: response.fieldsSet.join(", "), collection: response.collection })
          : t("DATA_CONSOLE_DIALOG.NO_DOCUMENT_MATCHED_THIS"),
      });
    } catch (error) {
      setResult(null);
      setFormError(error instanceof Error ? error.message : t("DATA_CONSOLE_DIALOG.THE_FIELD_COULD_NOT_BE"));
    }
  };

  return (
    <div className="space-y-4">
      <div className="space-y-1.5">
        <Label htmlFor="data-console-update-collection">{t("DATA_CONSOLE_DIALOG.COLLECTION")}</Label>
        <Select value={collectionName} onValueChange={selectCollection}>
          <SelectTrigger id="data-console-update-collection">
            <SelectValue placeholder={t("DATA_CONSOLE_DIALOG.CHOOSE_A_COLLECTION")} />
          </SelectTrigger>
          <SelectContent>
            {updatableCollections.map((entry) => (
              <SelectItem key={entry.logicalName} value={entry.logicalName}>
                {entry.logicalName}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {collection && (
        <div className="space-y-3">
          {collection.updatableFields.map((field) => (
            <div className="space-y-1.5" key={field}>
              <Label htmlFor={`data-console-field-${field}`}>{field}</Label>
              <Input
                id={`data-console-field-${field}`}
                value={values[field] ?? ""}
                onChange={(event) =>
                  setValues((current) => ({ ...current, [field]: event.target.value }))
                }
                placeholder={t("DATA_CONSOLE_DIALOG.ISO_8601_UTC_E_G")}
              />
            </div>
          ))}
          <p className="text-xs text-muted-foreground">
            {t("DATA_CONSOLE_DIALOG.LEAVE_A_FIELD_BLANK_TO")}
          </p>
        </div>
      )}

      {formError && <p className="text-sm text-destructive">{formError}</p>}

      {result && (
        <div className="flex flex-wrap items-center gap-2 rounded-md border p-2.5 text-xs">
          <Badge variant={result.modified ? "success" : "secondary"} className="font-normal">
            {result.modified ? t("DATA_CONSOLE_DIALOG.MODIFIED") : t("DATA_CONSOLE_DIALOG.NO_MATCH")}
          </Badge>
          <span className="text-muted-foreground">
            {result.collection}
            {result.fieldsSet.length > 0 ? ` · ${result.fieldsSet.join(", ")}` : ""}
          </span>
        </div>
      )}

      <Button onClick={submit} disabled={isPending || !collection}>
        {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
        {t("common:UPDATE")}
      </Button>
    </div>
  );
};

export const DataConsoleDialog = ({
  subscriptionId,
  organizationId,
  open,
  onOpenChange,
}: {
  subscriptionId: string;
  organizationId: string | undefined;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) => {
  const { t } = useT("utilities.subscription-simulation");
  const { data: policy, error, isError, isLoading } = useDataConsolePolicy(open);

  const readableCollections = (policy ?? [])
    .filter((entry) => entry.canRead)
    .map((entry) => entry.logicalName);
  const updatableCollections = (policy ?? [])
    .filter((entry) => entry.updatableFields.length > 0)
    .map((entry) => ({ logicalName: entry.logicalName, updatableFields: entry.updatableFields }));

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[85vh] w-[95vw] max-w-3xl overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{t("DATA_CONSOLE_DIALOG.DATA_CONSOLE")}</DialogTitle>
          <DialogDescription>
            {t("DATA_CONSOLE_DIALOG.A_NARROW_ALLOWLISTED_READ_UPDATE")}
          </DialogDescription>
        </DialogHeader>

        {isLoading ? (
          <div className="space-y-2">
            <Skeleton className="h-10 w-full rounded-md" />
            <Skeleton className="h-24 w-full rounded-md" />
          </div>
        ) : isError ? (
          <div className="flex flex-col items-start gap-2">
            <div className="flex items-center gap-2 text-destructive">
              <AlertCircle className="h-4 w-4" />
              <span className="font-medium">{t("DATA_CONSOLE_DIALOG.THE_DATA_CONSOLE_POLICY_COULD")}</span>
            </div>
            <p className="text-sm text-muted-foreground">
              {error instanceof Error
                ? error.message
                : t("DATA_CONSOLE_DIALOG.THE_DATA_CONSOLE_MAY_NOT")}
            </p>
          </div>
        ) : (
          <Tabs defaultValue="find">
            <TabsList>
              <TabsTrigger value="find">{t("DATA_CONSOLE_DIALOG.FIND")}</TabsTrigger>
              <TabsTrigger value="update">{t("common:UPDATE")}</TabsTrigger>
            </TabsList>
            <TabsContent value="find" className="pt-4">
              <FindTab
                subscriptionId={subscriptionId}
                organizationId={organizationId}
                readableCollections={readableCollections}
              />
            </TabsContent>
            <TabsContent value="update" className="pt-4">
              <UpdateTab
                subscriptionId={subscriptionId}
                organizationId={organizationId}
                updatableCollections={updatableCollections}
              />
            </TabsContent>
          </Tabs>
        )}
      </DialogContent>
    </Dialog>
  );
};
