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
  const { mutateAsync, isPending } = useFindData();

  const [collection, setCollection] = useState(readableCollections[0] ?? "");
  const [limit, setLimit] = useState("20");
  const [formError, setFormError] = useState<string | null>(null);
  const [result, setResult] = useState<SubscriptionSimulationDataQueryResponse | null>(null);

  const submit = async () => {
    setFormError(null);

    const parsedLimit = Number(limit);
    if (!collection) {
      setFormError("Choose a collection.");
      return;
    }
    if (!Number.isInteger(parsedLimit) || parsedLimit < 1 || parsedLimit > 100) {
      setFormError("Limit must be a whole number between 1 and 100.");
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
      setFormError(error instanceof Error ? error.message : "The data could not be read.");
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end gap-3">
        <div className="min-w-48 flex-1 space-y-1.5">
          <Label htmlFor="data-console-find-collection">Collection</Label>
          <Select value={collection} onValueChange={setCollection}>
            <SelectTrigger id="data-console-find-collection">
              <SelectValue placeholder="Choose a collection" />
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
          <Label htmlFor="data-console-find-limit">Limit</Label>
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
          Find
        </Button>
      </div>

      {formError && <p className="text-sm text-destructive">{formError}</p>}

      {result && (
        <div className="space-y-2">
          <p className="text-xs text-muted-foreground">
            {result.count} document{result.count === 1 ? "" : "s"} from {result.collection}.
          </p>
          {result.documents.length === 0 ? (
            <p className="py-6 text-center text-sm text-muted-foreground">
              No matching documents.
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
      setFormError("Choose a collection.");
      return;
    }
    if (Object.keys(fields).length === 0) {
      setFormError("Set at least one field.");
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
        title: response.modified ? "Field updated" : "Nothing matched",
        description: response.modified
          ? `Set ${response.fieldsSet.join(", ")} on ${response.collection}.`
          : "No document matched this subscription in that collection.",
      });
    } catch (error) {
      setResult(null);
      setFormError(error instanceof Error ? error.message : "The field could not be updated.");
    }
  };

  return (
    <div className="space-y-4">
      <div className="space-y-1.5">
        <Label htmlFor="data-console-update-collection">Collection</Label>
        <Select value={collectionName} onValueChange={selectCollection}>
          <SelectTrigger id="data-console-update-collection">
            <SelectValue placeholder="Choose a collection" />
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
                placeholder="ISO 8601 UTC, e.g. 2026-01-01T00:00:00Z"
              />
            </div>
          ))}
          <p className="text-xs text-muted-foreground">
            Leave a field blank to leave it unchanged. Every value here is parsed as a UTC
            timestamp — it is the only type any updatable field on this collection holds.
          </p>
        </div>
      )}

      {formError && <p className="text-sm text-destructive">{formError}</p>}

      {result && (
        <div className="flex flex-wrap items-center gap-2 rounded-md border p-2.5 text-xs">
          <Badge variant={result.modified ? "success" : "secondary"} className="font-normal">
            {result.modified ? "Modified" : "No match"}
          </Badge>
          <span className="text-muted-foreground">
            {result.collection}
            {result.fieldsSet.length > 0 ? ` · ${result.fieldsSet.join(", ")}` : ""}
          </span>
        </div>
      )}

      <Button onClick={submit} disabled={isPending || !collection}>
        {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
        Update
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
          <DialogTitle>Data console</DialogTitle>
          <DialogDescription>
            A narrow, allowlisted read/update surface over Mongo for whatever the actions above
            cannot reach. Never a raw query — every call is scoped server-side to one collection
            from the allowlist, one tenant, one organization and this one subscription, and every
            write is restricted to a small set of UTC-timestamp fields.
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
              <span className="font-medium">The data console policy could not be loaded</span>
            </div>
            <p className="text-sm text-muted-foreground">
              {error instanceof Error
                ? error.message
                : "The data console may not be enabled in this environment."}
            </p>
          </div>
        ) : (
          <Tabs defaultValue="find">
            <TabsList>
              <TabsTrigger value="find">Find</TabsTrigger>
              <TabsTrigger value="update">Update</TabsTrigger>
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
