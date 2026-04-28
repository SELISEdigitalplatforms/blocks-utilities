import { useParams } from "react-router-dom";
import { OrganizationDetail } from "@blocks-idp/iam/pages/organization-detail/organization-detail";

export default function IamOrgDetailPage() {
	const { itemId } = useParams<{ itemId: string }>();
	return <OrganizationDetail id={itemId!} />;
}
