import { EmailConfiguration } from "@blocks-communication/mail/email/email-configure/email-configure";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";

export default function EmailConfigurePage() {
  BREADCRUMB_CUSTOM_TITLES["/email"] = "Email";
  BREADCRUMB_CUSTOM_TITLES["/email/configure"] = "Configure";

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="hidden md:flex">
        <PageBreadcrumb breadcrumbIndex={2} />
      </div>
      <EmailConfiguration />
    </div>
  );
}
