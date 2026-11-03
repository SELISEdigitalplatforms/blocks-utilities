import { useEffect, useState } from "react";
import { AlertCircle, ChevronLeft, ChevronRight, FileText, Pencil, Plus, Search, Trash2 } from "lucide-react";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { Input } from "@/components/ui-kits/input/input";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui-kits/table/table";
import { toast } from "@/hooks/use-toast";
import { SmsPageHeader } from "../components/sms-page-header";
import { SmsTemplateDialog } from "../components/sms-template-dialog";
import { SMS_TEMPLATE_PAGE_SIZE } from "../constants/sms.constants";
import { useDeleteSmsTemplate, useSmsTemplates } from "../hooks/use-sms";
import type { SmsTemplate } from "../models/sms.model";
import { useT } from "@seliseblocks/genesis-os";

// Debounced so each keystroke does not become a request.
const useDebounced = (value: string, delay = 300) => {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);
  return debounced;
};

export const SmsTemplatesPage = () => {
  const { t } = useT("utilities.sms");
  const [search, setSearch] = useState("");
  const [language, setLanguage] = useState("");
  const [page, setPage] = useState(1);
  const debouncedSearch = useDebounced(search);
  const debouncedLanguage = useDebounced(language);
  const [editing, setEditing] = useState<SmsTemplate | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [deleting, setDeleting] = useState<SmsTemplate | null>(null);
  const deleteTemplate = useDeleteSmsTemplate();

  const { data, error, isError, isLoading, isFetching, refetch } = useSmsTemplates({
    search: debouncedSearch,
    language: debouncedLanguage,
    page,
    pageSize: SMS_TEMPLATE_PAGE_SIZE,
  });
  const totalPages = Math.max(1, Math.ceil((data?.totalCount ?? 0) / SMS_TEMPLATE_PAGE_SIZE));
  const hasFilters = Boolean(debouncedSearch || debouncedLanguage);

  const openEditor = (template: SmsTemplate | null) => {
    setEditing(template);
    setDialogOpen(true);
  };

  const confirmDelete = async () => {
    if (!deleting) return;
    try {
      await deleteTemplate.mutateAsync(deleting.itemId);
      toast({ variant: "success", title: t("SMS_TEMPLATES_PAGE.TEMPLATE_DELETED"), description: `${deleting.name} (${deleting.language})` });
      setDeleting(null);
    } catch (deleteError) {
      toast({
        variant: "destructive",
        title: t("SMS_TEMPLATES_PAGE.TEMPLATE_NOT_DELETED"),
        description: deleteError instanceof Error ? deleteError.message : undefined,
      });
    }
  };

  return (
    <main className="min-w-0 space-y-5 p-4 sm:p-6 lg:p-8">
      <SmsPageHeader
        title={t("SMS_TEMPLATES_PAGE.SMS_TEMPLATES")}
        description={t("SMS_TEMPLATES_PAGE.REUSABLE_MESSAGES_WITH")}
        icon={<FileText className="h-6 w-6" />}
        actions={
          <Button onClick={() => openEditor(null)}>
            <Plus className="mr-2 h-4 w-4" />
            {t("SMS_TEMPLATES_PAGE.NEW_TEMPLATE")}
          </Button>
        }
      />

      <Card className="rounded-xl p-0">
        <div className="flex flex-col gap-3 border-b p-4 sm:flex-row sm:items-center sm:justify-between sm:p-5">
          <div>
            <h2 className="font-semibold">{t("SMS_TEMPLATES_PAGE.TEMPLATES")}</h2>
            <p className="text-xs text-muted-foreground">{data ? t("SMS_TEMPLATES_PAGE.IN_TOTAL", { totalCount: data.totalCount }) : " "}</p>
          </div>
          <div className="flex w-full flex-col gap-2 sm:w-auto sm:flex-row">
            <div className="relative min-w-0 sm:w-64">
              <Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
              <Input
                value={search}
                onChange={(event) => {
                  setSearch(event.target.value);
                  setPage(1);
                }}
                placeholder={t("SMS_TEMPLATES_PAGE.SEARCH_BY_NAME")}
                className="pl-9"
                aria-label={t("SMS_TEMPLATES_PAGE.SEARCH_TEMPLATES")}
              />
            </div>
            <Input
              value={language}
              onChange={(event) => {
                setLanguage(event.target.value);
                setPage(1);
              }}
              placeholder={t("SMS_TEMPLATES_PAGE.LANGUAGE_E_G_EN_US")}
              className="sm:w-44"
              aria-label={t("SMS_TEMPLATES_PAGE.FILTER_BY_LANGUAGE")}
            />
          </div>
        </div>

        {isLoading ? (
          <div className="space-y-3 p-5" aria-label={t("SMS_TEMPLATES_PAGE.LOADING_TEMPLATES")}>
            {Array.from({ length: 4 }, (_, index) => (
              <Skeleton key={index} className="h-12 w-full" />
            ))}
          </div>
        ) : isError ? (
          <div className="flex min-h-72 flex-col items-center justify-center px-5 text-center">
            <span className="rounded-full bg-destructive/10 p-4 text-destructive">
              <AlertCircle className="h-7 w-7" />
            </span>
            <h3 className="mt-4 text-lg font-semibold">{t("SMS_TEMPLATES_PAGE.TEMPLATES_COULD_NOT_BE_LOADED")}</h3>
            <p className="mt-1 max-w-md text-sm text-muted-foreground">{error instanceof Error ? error.message : ""}</p>
            <Button className="mt-5" variant="outline" onClick={() => refetch()}>
              {t("SMS_TEMPLATES_PAGE.TRY_AGAIN")}
            </Button>
          </div>
        ) : !data?.items.length ? (
          <div className="flex min-h-72 flex-col items-center justify-center px-5 text-center">
            <span className="rounded-full bg-muted p-4 text-muted-foreground">
              <FileText className="h-7 w-7" />
            </span>
            <h3 className="mt-4 text-lg font-semibold">{hasFilters ? t("SMS_TEMPLATES_PAGE.NO_TEMPLATES_MATCH") : t("SMS_TEMPLATES_PAGE.NO_TEMPLATES_YET")}</h3>
            <p className="mt-1 max-w-md text-sm text-muted-foreground">
              {hasFilters ? t("SMS_TEMPLATES_PAGE.CHANGE_THE_SEARCH_OR_LANGUAGE") : t("SMS_TEMPLATES_PAGE.CREATE_ONE_TO_SEND_MESSAGES")}
            </p>
            {!hasFilters && (
              <Button className="mt-5" onClick={() => openEditor(null)}>
                <Plus className="mr-2 h-4 w-4" />
                {t("SMS_TEMPLATES_PAGE.NEW_TEMPLATE")}
              </Button>
            )}
          </div>
        ) : (
          <>
            <Table className={isFetching ? "opacity-70" : undefined}>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("common:NAME")}</TableHead>
                  <TableHead>{t("SMS_TEMPLATES_PAGE.LANGUAGE")}</TableHead>
                  <TableHead>{t("SMS_TEMPLATES_PAGE.MESSAGE")}</TableHead>
                  <TableHead>{t("SMS_TEMPLATES_PAGE.PLACEHOLDERS")}</TableHead>
                  <TableHead>{t("SMS_TEMPLATES_PAGE.UPDATED")}</TableHead>
                  <TableHead className="text-right">{t("common:ACTIONS")}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.items.map((template) => (
                  <TableRow key={template.itemId}>
                    <TableCell className="font-medium">{template.name}</TableCell>
                    <TableCell>{template.language}</TableCell>
                    <TableCell>
                      <span className="block max-w-72 truncate text-muted-foreground" title={template.body}>
                        {template.body}
                      </span>
                    </TableCell>
                    <TableCell>
                      <div className="flex max-w-56 flex-wrap gap-1">
                        {template.placeholders.length
                          ? template.placeholders.map((key) => (
                              <Badge key={key} variant="info">
                                {key}
                              </Badge>
                            ))
                          : "—"}
                      </div>
                    </TableCell>
                    <TableCell className="whitespace-nowrap text-muted-foreground">
                      {new Date(template.lastUpdatedDate).toLocaleDateString()}
                    </TableCell>
                    <TableCell>
                      <div className="flex justify-end gap-2">
                        <Button variant="outline" size="sm" onClick={() => openEditor(template)}>
                          <Pencil className="mr-2 h-4 w-4" />
                          {t("common:EDIT")}
                        </Button>
                        <Button variant="outline" size="sm" onClick={() => setDeleting(template)} aria-label={t("SMS_TEMPLATES_PAGE.DELETE", { name: template.name })}>
                          <Trash2 className="h-4 w-4" />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            <div className="flex items-center justify-end gap-2 border-t p-3 text-sm">
              <span className="text-muted-foreground">
                {t("SMS_TEMPLATES_PAGE.PAGE_OF", { page, total: totalPages })}
              </span>
              <Button variant="outline" size="sm" onClick={() => setPage((p) => p - 1)} disabled={page <= 1} aria-label={t("SMS_TEMPLATES_PAGE.PREVIOUS_PAGE")}>
                <ChevronLeft className="h-4 w-4" />
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() => setPage((p) => p + 1)}
                disabled={page >= totalPages}
                aria-label={t("SMS_TEMPLATES_PAGE.NEXT_PAGE")}
              >
                <ChevronRight className="h-4 w-4" />
              </Button>
            </div>
          </>
        )}
      </Card>

      <SmsTemplateDialog open={dialogOpen} template={editing} onOpenChange={setDialogOpen} />

      <Dialog open={deleting !== null} onOpenChange={(open) => !open && setDeleting(null)}>
        <ConfirmationModal
          data={{
            dialogTitle: t("SMS_TEMPLATES_PAGE.DELETE_TEMPLATE"),
            dialogSubtitle: t("SMS_TEMPLATES_PAGE.DELETE_CONFIRM", { name: deleting?.name ?? "", language: deleting?.language ?? "" }),
            confirmButton: t("common:DELETE"),
          }}
          onCancel={() => setDeleting(null)}
          onConfirm={confirmDelete}
          buttonState={{ confirm: { disable: deleteTemplate.isPending } }}
        />
      </Dialog>
    </main>
  );
};
