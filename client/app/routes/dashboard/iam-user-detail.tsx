import { useParams } from "react-router-dom";
import { User } from "@blocks-idp/iam/modules/user-management";

export default function IamUserDetailPage() {
	const { id } = useParams<{ id: string }>();
	return <User id={id!} />;
}
