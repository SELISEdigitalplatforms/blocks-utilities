import { useSearchParams } from "react-router-dom";
import { SSOConfiguration } from "@blocks-idp/authentication/pages/sso-configuration";

export default function SsoConfigurationPage() {
	const [searchParams] = useSearchParams();
	const provider = searchParams.get("provider");
	const id = searchParams.get("id") || "";
	return (
		<div className="p-6">
			<SSOConfiguration params={{ provider: provider as any, id }} />
		</div>
	);
}
