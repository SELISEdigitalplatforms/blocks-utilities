import { useParams } from "react-router-dom";
import { EmailCommunicationDetails } from "@blocks-communication/mail/email/email-communication-details/email-communication-details";

export default function EmailCommunicationDetailsPage() {
  const { id } = useParams<{ id: string }>();
  
  return <EmailCommunicationDetails params={{ id: id || "" }} />;
}
