

import React from "react";
import { ArrowLeft } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { formatDate } from "@/lib/utils";
import { useGetEmailUsageById } from "@blocks-communication/mail/hooks/use-email-usage";
import { StatusBadge } from "@blocks-communication/mail/email/email-usage/status-badge";
import { EmailUsageDetailsSkeleton } from "@blocks-communication/mail/email/email-usage/email-usage-details-skeleton";
import { EmailUsageDetailsBreadcrumb } from "@blocks-communication/mail/email/email-usage/email-usage-details-breadcrumb";

export const EmailUsageDetails = ({ id }: { id: string }) => {
  const navigate = useNavigate();
  const { data: details, isLoading } = useGetEmailUsageById(id);
  if (isLoading) return <EmailUsageDetailsSkeleton />;

  if (!details) return <div>Email details not found.</div>;

  return (
    <main className="flex flex-col gap-6">
      <div className="hidden md:flex">
        <EmailUsageDetailsBreadcrumb id={details.messageId || id} isInbound={details.isInbound} />
      </div>

      <div className="mt-5 flex items-center gap-2">
        <Button size="icon" variant="ghost" className="h-8 w-8" onClick={() => navigate(-1)}>
          <ArrowLeft className="h-6 w-6" />
        </Button>
        <h1 className="text-lg font-semibold md:text-2xl">Email Details</h1>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Details</CardTitle>
        </CardHeader>
        <CardContent className="space-y-6">
          <div className="grid grid-cols-1 gap-6 md:grid-cols-2 lg:grid-cols-4">
            <div>
              <p className="text-sm text-muted-foreground">From</p>
              <p className="mt-1 font-medium">{details.from || "-"}</p>
            </div>
            <div>
              <p className="text-sm text-muted-foreground">To</p>
              <p className="mt-1 font-medium">{details.to || "-"}</p>
            </div>
            <div>
              <p className="text-sm text-muted-foreground">
                {details.isInbound ? "Received Date" : "Send Date"}
              </p>
              <p className="mt-1 font-medium">
                {details.date ? formatDate(new Date(details.date)) : "-"}
              </p>
            </div>
            <div className="lg:col-span-2">
              <p className="text-sm text-muted-foreground">Subject</p>
              <p className="mt-1 font-medium">{details.subject || "-"}</p>
            </div>
            <div>
              <p className="text-sm text-muted-foreground">Status</p>
              <div className="mt-1">
                <StatusBadge status={details.status} />
              </div>
            </div>
            {details.error && (
              <div className="lg:col-span-4">
                <p className="text-sm text-error text-muted-foreground">Error</p>
                <p className="mt-1 font-medium text-error">{details.error}</p>
              </div>
            )}
          </div>
        </CardContent>
      </Card>
      <Card className="p-0">
        <CardContent className="space-y-6">
          <CardHeader className="px-5 pb-0 pt-4">
            <CardTitle>Email Body</CardTitle>
          </CardHeader>
          <div>
            <div className="rounded-md border-t p-4">
              <iframe srcDoc={details.body || "-"} style={{ width: "100%", height: "60vh" }} />
            </div>
          </div>
        </CardContent>
      </Card>
    </main>
  );
};
