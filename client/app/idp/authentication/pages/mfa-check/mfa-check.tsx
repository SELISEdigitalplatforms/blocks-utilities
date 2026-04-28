
import { Logo } from "@/components/logo";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";
import { parseAsInteger, useQueryStates } from "nuqs";
import { MfaCheckFrom } from "./mfa-check-form";

export const MfaCheck = () => {
  const [{ mfa_type }] = useQueryStates({
    mfa_type: parseAsInteger.withDefault(0),
  });
  const mfa_type_message =
    mfa_type == 1
      ? "2-step verification enabled. Open your authenticator app and get the verification code. Enter the code here."
      : "2-step verification enabled. Check your email for the verification code. Enter the code here to continue.";

  return (
    <div className="flex min-h-screen flex-col items-center bg-background">
      <div className="mb-4 mt-[136px] p-4">
        <Logo src={"/Logo.svg"} width={128} height={54.931} />
      </div>
      <Card className="mx-auto mt-16 w-full rounded border-solid border-background py-5 shadow-none sm:max-w-md sm:border-[#95ADC4]">
        <CardHeader className="text-center">
          <CardTitle className="text-3xl leading-9">Verify it’s you</CardTitle>
          <CardDescription className="mt-3 text-xl font-normal leading-7 text-high-emphasis">
            {mfa_type_message}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <MfaCheckFrom />
        </CardContent>
      </Card>
    </div>
  );
};
