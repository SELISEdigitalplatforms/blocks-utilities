import { useSearchParams } from "react-router";
import { SSOConfiguration } from "@blocks-idp/authentication/pages/sso-configuration";
import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants";

export default function SsoConfigurationPage() {
	const [searchParams] = useSearchParams();
	const provider = searchParams.get("provider");
	const id = searchParams.get("id") || "";
	return (
		<div className="p-6">
			<SSOConfiguration params={{ provider: provider as unknown as SSO_PROVIDERS, id }} />
		</div>
	);
}
