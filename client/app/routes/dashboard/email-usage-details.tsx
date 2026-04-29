import { useParams } from "react-router-dom";
import { EmailUsageDetails } from "@blocks-communication/mail/email/email-usage/email-usage-details";

export default function EmailUsageDetailsPage() {
  const { id } = useParams<{ id: string }>();
  
  return <EmailUsageDetails id={id || ""} />;
}
