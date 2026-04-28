import { UserBasicInformation } from "../user-basic-information";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { ProfileImageUploader } from "../profile-image-uploader";
import { ProfileMFA } from "../profile-mfa";

const x_blocks_key = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";

export const ProfileDetails = ({ id }: { id: string }) => {
  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-12 xl:gap-8">
      <ProfileImageUploader id={id} projectKey={x_blocks_key} />
      <div className="lg:col-span-9">
        <UserBasicInformation id={id} projectKey={x_blocks_key} />
        <div className="mt-5">
          <ProfileMFA userId={id} projectKey={x_blocks_key} />
        </div>
      </div>
    </div>
  );
};
