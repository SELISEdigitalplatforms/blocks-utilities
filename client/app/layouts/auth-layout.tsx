import { Outlet } from "react-router-dom";
import { Suspense } from "react";
import { PublicGuard } from "@/guards/public-guard";
import { Logo } from "@/components/logo";
import { BlockInfo } from "@blocks-idp/authentication/components/auth-layout/blocks-info";

export function AuthLayout() {
  return (
    <Suspense>
      <PublicGuard>
        <div className="flex min-h-screen flex-col items-center py-[24px] lg:py-[64px] xl:px-[154px]">
          <div className="flex w-full items-center justify-center">
            <Logo width={128} height={55} />
          </div>
          <div className="mt-[20px] flex w-full flex-col justify-center gap-0 md:px-[24px] lg:mt-[70px] lg:flex-row lg:gap-20 lg:px-0 2xl:mt-[80px]">
            <div>
              <Outlet />
            </div>
            <BlockInfo />
          </div>
        </div>
      </PublicGuard>
    </Suspense>
  );
}
