import { Logo } from "@/components/logo";
import { Button } from "@/components/ui-kits/button/button";
import { Link } from "react-router-dom";

export const ActivationSuccess = () => {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-background p-4 md:justify-start">
      <div className="mb-4 p-4 md:mt-[136px]">
        <Logo src={"/Logo.svg"} width={128} height={54.931} />
      </div>
      <div className="text-center md:mt-[80px]">
        <h3 className="my-6 text-2xl font-bold tracking-tight sm:text-3xl">
          You have successfully activated your account
        </h3>
        <p className="text-justify text-lg text-foreground sm:text-center sm:text-xl">
          Please, continue to login with your password and unlock a library of open source services
        </p>
        <Button className="mt-4">
          <Link to="/login">Log in</Link>
        </Button>
      </div>
    </div>
  );
};
