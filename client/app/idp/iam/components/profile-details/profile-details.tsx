import { UserBasicInformation } from "@/idp/iam/components/user-basic-information/user-basic-information";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { ProfileImageUploader } from "@/idp/iam/components/profile-image-uploader/profile-image-uploader";
import { ProfileMFA } from "@/idp/iam/components/profile-mfa/profile-mfa";
const xBlocksKey = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";
export const ProfileDetails = ({ id }: { id: string }) => {
  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-12 xl:gap-8">
      <ProfileImageUploader id={id} projectKey={xBlocksKey} />
      <div className="lg:col-span-9">
        <UserBasicInformation id={id} projectKey={xBlocksKey} own />
        <div className="mt-5">
          <ProfileMFA userId={id} projectKey={xBlocksKey} />
        </div>
      </div>
    </div>
  );
};
