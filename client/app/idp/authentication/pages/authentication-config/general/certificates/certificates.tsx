

import { useState } from "react";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { useProjectStore } from "@/store/useProjectStore";
import { useGetSavedPublicCertificates } from "@blocks-idp/authentication/hooks/use-identifier";
import { Waypoints } from "lucide-react";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { EmptyConfiguration } from "./empty-configuration";
import { AddEditProviderModal } from "./add-edit-provider-modal";
import { providers } from "@blocks-idp/authentication/constants/authentication.constant";
import MapJwtClaimModal from "./map-jwt-claim-modal";
import { useGetJwtClaim } from "@blocks-idp/authentication/hooks/use-jwt-claim";

const LoadingSkelton = () => {
  return (
    <Card>
      <CardHeader className="mb-0">
        <div className="flex items-start justify-between">
          <div className="flex flex-col gap-2">
            <Skeleton className="h-6 w-32" />
            <Skeleton className="h-4 w-48" />
          </div>
          <Skeleton className="h-9 w-20" />
        </div>
      </CardHeader>
      <CardContent className="mt-4 space-y-4">
        {/* First Row: Provider and URL */}
        <div className="flex flex-col gap-4 md:flex-row md:gap-8">
          <div className="md:w-[20%]">
            <Skeleton className="mb-2 h-4 w-16" />
            <Skeleton className="h-5 w-32" />
          </div>
          <div className="flex-1">
            <Skeleton className="mb-2 h-4 w-16" />
            <Skeleton className="h-5 w-full max-w-md" />
          </div>
        </div>

        {/* Second Row: Issuer and Audience */}
        <div className="flex flex-col gap-4 md:flex-row md:gap-8">
          <div className="md:w-[20%]">
            <Skeleton className="mb-2 h-4 w-16" />
            <Skeleton className="h-5 w-40" />
          </div>
          <div className="flex-1">
            <Skeleton className="mb-2 h-4 w-16" />
            <Skeleton className="h-5 w-40" />
          </div>
        </div>
      </CardContent>
    </Card>
  );
};

export const Certificates = () => {
  const projectKey = useProjectStore().selectedProject?.tenantId ?? "";
  const { isLoading, data: existingCertificate } = useGetSavedPublicCertificates(projectKey);
  const { data: jwtClaimData, isLoading: isJwtClaimLoading } = useGetJwtClaim(
    { projectKey, itemId: "" },
    !!projectKey && !!existingCertificate?.isConfigured,
  );
  const [isJwtClaimModalOpen, setIsJwtClaimModalOpen] = useState<boolean>(false);
  const handleJwtClaim = () => {
    setIsJwtClaimModalOpen(true);
  };

  const hasJwtClaimData = !!jwtClaimData?.itemId;

  if (isLoading) {
    return <LoadingSkelton />;
  }

  if (!existingCertificate?.isConfigured) {
    return <EmptyConfiguration />;
  }

  return (
    <>
      {!isLoading && !isJwtClaimLoading && !hasJwtClaimData && (
        <div className="text-blocks-error mb-4 flex flex-col items-center justify-center gap-1 rounded-sm border border-base-error bg-blocks-error-100 px-4 py-4 text-base font-normal text-blocks-error-800 md:flex-row">
          You didn&apos;t map the jwt claims. To ignore 401(Unauthorized) in api request please{" "}
          <span className="underline">Map JWT Claims</span>.
        </div>
      )}
      <Card>
        <CardHeader className="mb-0">
          <div className="flex items-start justify-between">
            <div className="flex flex-col gap-2">
              <CardTitle>External IdP</CardTitle>
              <CardDescription>Your connected identity provider</CardDescription>
            </div>
            <div className="flex gap-2">
              <div className="flex gap-2">
                <Button onClick={handleJwtClaim} variant="outline" size="sm">
                  <Waypoints className="mr-2 h-4 w-4" />
                  Map JWT Claim
                </Button>
              </div>
              <div className="flex gap-2">
                <AddEditProviderModal existingData={existingCertificate} />
              </div>
            </div>
          </div>
        </CardHeader>

        <CardContent className="mt-4 space-y-4">
          {/* First Row: Provider and URL */}
          <div className="flex flex-col gap-4 md:flex-row md:gap-8">
            {/* Provider Section */}
            <div className="md:w-[20%]">
              <label className="mb-2 block text-sm text-gray-600">Provider</label>
              <div className="flex items-center gap-2 text-sm font-medium">
                {(() => {
                  const provider = providers.find(
                    (p) => p.name.toLowerCase() === existingCertificate.providerName?.toLowerCase(),
                  );
                  const isOthers = existingCertificate.providerName?.toLowerCase() === "others";

                  if (isOthers) {
                    return (
                      <div className="flex h-4 w-4 items-center justify-center rounded-full bg-blue-600">
                        <div className="h-2 w-2 rounded-full bg-white"></div>
                      </div>
                    );
                  } else if (provider?.icon) {
                    return (
                      <img
                        src={provider.icon}
                        alt={provider.name}
                        width={16}
                        height={16}
                        className="h-4 w-4 object-contain"
                      />
                    );
                  }
                  return null;
                })()}
                <span>{existingCertificate.providerName}</span>
              </div>
            </div>

            {/* URL Section */}
            <div className="flex-1">
              <label className="mb-2 block text-sm text-gray-600">URL</label>
              <div className="break-all text-sm font-medium">
                {existingCertificate.jwksUrl || existingCertificate.publicCertificatePath || "-"}
              </div>
            </div>
          </div>

          {/* Second Row: Issuer and Audience */}
          <div className="flex flex-col gap-4 md:flex-row md:gap-8">
            {/* Issuer Section */}
            <div className="md:w-[20%]">
              <label className="mb-2 block text-sm text-gray-600">Issuer</label>
              <div className="break-all text-sm font-medium">
                {existingCertificate.issuer || "-"}
              </div>
            </div>

            {/* Audience Section */}
            <div className="flex-1">
              <label className="mb-2 block text-sm text-gray-600">Audience</label>
              <div className="break-all text-sm font-medium">
                {existingCertificate.audiences?.length
                  ? existingCertificate.audiences.join(", ")
                  : "-"}
              </div>
            </div>
          </div>
        </CardContent>
      </Card>

      <MapJwtClaimModal open={isJwtClaimModalOpen} onOpenChange={setIsJwtClaimModalOpen} />
    </>
  );
};
