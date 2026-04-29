import { EmailServiceTable } from "@blocks-communication/mail/email/email-service-table/email-service-table";

export default function EmailPage() {
  return (
    <div className="flex flex-col gap-6 p-6">
      <EmailServiceTable />
    </div>
  );
}
