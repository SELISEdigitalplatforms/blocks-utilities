import { useParams } from "react-router-dom";
import { EditEmailTemplate } from "@blocks-utilities/mail/pages/email-template-edit/email-template-edit";

export default function EmailTemplateEditPage() {
  const { id } = useParams<{ id: string }>();
  
  return <EditEmailTemplate params={{ id: id || "" }} />;
}
