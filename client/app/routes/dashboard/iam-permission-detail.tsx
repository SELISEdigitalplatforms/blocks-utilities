import { useParams } from "react-router-dom";
import { PermissionDetails } from "@blocks-idp/iam/modules/permission-management/permission-details";

export default function IamPermissionDetailPage() {
	const { id } = useParams<{ id: string }>();
	return <PermissionDetails id={id!} />;
}
