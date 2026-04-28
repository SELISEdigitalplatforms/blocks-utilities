import React from "react";
import { UserBasicInformation } from "../user-basic-information";
import { useProjectStore } from "@/store/useProjectStore";
import { ProfileImageUploader } from "../profile-image-uploader";

export const UserDetails = ({ id }: { id: string }) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-12">
      <div className="col-span-full lg:col-span-3">
        <ProfileImageUploader id={id} projectKey={tenantId} />
      </div>
      <div className="lg:col-span-9">
        <UserBasicInformation
          id={id}
          projectKey={tenantId}
          detailsGridClassName={"md:grid-cols-2"}
        />
      </div>
    </div>
  );
};
