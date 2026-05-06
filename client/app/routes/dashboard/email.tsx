import { EmailServiceTable } from "@blocks-utilities/mail/pages/email-service-table/email-service-table";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";

export default function EmailPage() {
  return (
    <div className="flex flex-col gap-6 p-6">
      <PageBreadcrumb />
      <EmailServiceTable />
    </div>
  );
}
