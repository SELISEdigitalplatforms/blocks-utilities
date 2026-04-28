

import { Logo } from "@/components/logo";
import { Button } from "@/components/ui-kits/button/button";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { Link } from "react-router-dom";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";
import {
  useAccountActivationCodeExpiration,
  useAccountResendActivation,
} from "@blocks-idp/iam/hooks/use-account";
import { AlertTriangle, CheckCircle2 } from "lucide-react";
import { useEffect, useState } from "react";
import { ActivationForm } from "./activation-form";

type ActivationProps = {
  code?: string;
  lang?: string;
};

const x_blocks_key = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY");

export const Activation = ({ code }: ActivationProps) => {
  const { isPending: isActivationPending, mutateAsync: activationCodeValidation } =
    useAccountActivationCodeExpiration();
  const { mutateAsync: resendActivationLink, isPending: isResendPending } =
    useAccountResendActivation();

  const [isValidCode, setIsValidCode] = useState<boolean | null>(null);
  const [activationError, setActivationError] = useState<"invalid" | "expired" | null>(null);
  const [activationUserId, setActivationUserId] = useState<string | null>(null);
  const [resendMessage, setResendMessage] = useState<string | null>(null);
  const [resendSuccess, setResendSuccess] = useState(false);

  useEffect(() => {
    if (!code) {
      setActivationError("invalid");
      setActivationUserId(null);
      setResendMessage(null);
      setResendSuccess(false);
      setIsValidCode(false);
      return;
    }

    const validateCode = async () => {
      try {
        const res = await activationCodeValidation({
          projectKey: x_blocks_key as string,
          activationCode: code,
        });

        if (res.errors != null) {
          // Invalid code, doesn't exist code
          setActivationError("invalid");
          setActivationUserId(null);
          setResendMessage(null);
          setResendSuccess(false);
        } else if (res.userId != null) {
          // Code expired, resend activation link using this userId
          setActivationError("expired");
          setActivationUserId(res.userId);
          setResendMessage(null);
          setResendSuccess(false);
        } else {
          // activation code is valid, show the activate account component
          setActivationError(null);
          setActivationUserId(null);
          setResendMessage(null);
          setResendSuccess(false);
        }

        setIsValidCode(res.isSuccess);
      } catch {
        setActivationError("invalid");
        setActivationUserId(null);
        setResendMessage(null);
        setResendSuccess(false);
        setIsValidCode(false);
      }
    };

    validateCode();
  }, [code, activationCodeValidation]);

  const handleResendActivation = async () => {
    if (!activationUserId || isResendPending) return;

    try {
      setResendMessage(null);
      setResendSuccess(false);

      const response = await resendActivationLink({
        userId: activationUserId,
        projectKey: x_blocks_key as string,
      });

      if (response?.isSuccess) {
        setResendSuccess(true);
        setResendMessage("A new activation link has been sent to your email.");
      } else {
        setResendSuccess(false);
        setResendMessage("Failed to resend activation link. Please try again later.");
      }
    } catch (error) {
      setResendSuccess(false);
      setResendMessage(
        error instanceof Error ? error.message : "Failed to resend activation link.",
      );
    }
  };

  if (isActivationPending || isValidCode === null) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <p>Validating activation code...</p>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col items-center bg-background">
      <Link to="/login" className="mb-4 mt-[30px] cursor-pointer p-4 hover:opacity-80 transition-opacity">
        <Logo src={"/Logo.svg"} width={128} height={54.931} />
      </Link>
      {activationError === null ? (
        <>
          <Card className="mx-auto w-full rounded border-solid border-background shadow-none sm:max-w-md sm:border-[#95ADC4]">
            <CardHeader className="text-center">
              <CardTitle className="text-3xl leading-9">Activate account</CardTitle>
              <CardDescription className="mt-2 text-xl text-foreground">
                Complete your account setup
              </CardDescription>
            </CardHeader>
            <CardContent>
              <ActivationForm code={code ?? ""} />
            </CardContent>
          </Card>
        </>
      ) : activationError === "invalid" ? (
        <Card className="mx-auto w-full max-w-lg rounded-none border-none text-center shadow-none">
          <CardContent className="p-8">
            <AlertTriangle className="mx-auto flex h-10 w-10 items-center justify-center text-amber-600" />

            <h1 className="text-xl font-semibold">Invalid Activation Link</h1>

            <p className="mt-2 text-sm">
              The activation code is invalid. Please check the link or request a new activation
              email from your administrator.
            </p>
          </CardContent>
        </Card>
      ) : (
        <Card className="mx-auto w-full max-w-lg rounded-none border-none text-center shadow-none">
          <CardContent className="p-8">
            {/* <div className="mx-auto mb-4 flex h-12 w-12 items-center justify-center rounded-full border"> */}
            <AlertTriangle className="mx-auto flex h-10 w-10 items-center justify-center text-amber-600" />
            {/* </div> */}

            <h1 className="text-xl font-semibold">Activation Link Expired</h1>

            <p className="mt-2 text-sm">
              This activation link has expired and can&apos;t be used anymore. Please request a new
              link to complete your account activation.
            </p>

            <div className="mt-4 flex flex-col items-center gap-2">
              <Button
                onClick={handleResendActivation}
                disabled={!activationUserId || isResendPending || resendSuccess}
              >
                Resend activation link
              </Button>
              {resendMessage ? (
                <div className="flex items-center gap-2 text-sm">
                  {resendSuccess ? (
                    <CheckCircle2 className="h-4 w-4 text-success" />
                  ) : (
                    <AlertTriangle className="h-4 w-4 text-destructive" />
                  )}
                  <span className={resendSuccess ? "text-success" : "text-destructive"}>
                    {resendMessage}
                  </span>
                </div>
              ) : null}
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  );
};
