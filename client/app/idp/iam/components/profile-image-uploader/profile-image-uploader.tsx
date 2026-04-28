import { useEffect, useRef, useState } from "react";

import { Camera } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { useGetPreSignedUrlForUpload, useUploadFile } from "@blocks-storage/hooks/use-storage-file";
import { storageService } from "@blocks-storage/services/storage.service";
import { useGetUserById, useUpdateUser } from "@blocks-idp/iam/hooks/use-user";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";

const emptyProfilePhoto = "/assets/images/empty-profile-photo.png";
import { ModuleName } from "@/constants/modules.constants";

type ProfileImageUploaderProps = { projectKey: string; id: string };

export const ProfileImageUploader = ({ projectKey, id }: ProfileImageUploaderProps) => {
  const [image, setImage] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const { data } = useGetUserById({ id, projectKey });
  const { mutateAsync } = useGetPreSignedUrlForUpload();
  const { mutateAsync: uploadImageMutate } = useUploadFile();
  const { mutateAsync: updateUserMutate } = useUpdateUser({ projectKey, id, own: true });
  const [isProfileImageUploading, setIsProfileImageUploading] = useState<boolean>(false);

  useEffect(() => {
    if (data?.data) {
      setImage(data.data.profileImageUrl);
    }
  }, [data?.data, data?.data.profileImageUrl]);

  const uploadImage = async (file: File) => {
    try {
      setIsProfileImageUploading(true);
      // const profileImageId = "BLK-" + new Date().getTime().toString();
      const res = await mutateAsync({
        itemId: "",
        accessModifier: "Public",
        configurationName: "Default",
        name: file.name,
        projectKey,
        tags: "",
        metaData: "",
        parentDirectoryId: "",
        moduleName: ModuleName.IAMCloud,
      });
      if (!res.isSuccess) return;
      const profileImageId = res.fileId;
      await uploadImageMutate({ url: res.uploadUrl, file });
      const userProfileFile = await storageService.file.getFileByFileId({
        itemId: profileImageId,
        projectKey,
      });
      const updatedUser = await updateUserMutate({
        ...data?.data,
        itemId: id,
        projectKey,
        profileImageId: userProfileFile.itemId,
        profileImageUrl: userProfileFile.url,
      });
      if (!updatedUser.isSuccess) return showErrorToast({ errors: updatedUser.errors });
      showSuccessToast({ description: "Profile pic updated successfully" });
    } catch (error) {
      if (isErrorWithErrors(error)) {
        return showErrorToast({ errors: error.errors });
      }
      return showErrorToast({ errors: "Something wen wrong" });
    } finally {
      setIsProfileImageUploading(false);
    }
  };

  const handleFileChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const selectedFile = event.target.files?.[0];
    if (!selectedFile) return;

    const MAX_SIZE_MB = 5;
    const ALLOWED_TYPES = [
      "image/png",
      "image/jpeg",
      "image/jpg",
      "image/gif",
      "image/webp",
      "image/svg+xml",
    ];

    if (!ALLOWED_TYPES.includes(selectedFile.type)) {
      event.target.value = "";
      return showErrorToast({
        errors: "Only image files (PNG, JPG, GIF, WebP, and SVG) are allowed",
      });
    }

    if (selectedFile.size > MAX_SIZE_MB * 1024 * 1024) {
      event.target.value = "";
      return showErrorToast({ errors: `File size must be less than ${MAX_SIZE_MB}MB` });
    }

    setImage(URL.createObjectURL(selectedFile)); // Generate preview
    uploadImage(selectedFile);
    event.target.value = "";
  };

  return (
    <div className="flex flex-col items-center justify-center gap-8 lg:col-span-3 lg:justify-start">
      <div className="relative aspect-square w-full max-w-[200px] overflow-hidden rounded-full bg-gray-50 dark:bg-gray-800">
        {image ? (
          <>
            <img
              src={image}
              alt="Profile Image"
             
             
              className="rounded-full object-cover"
            />
            {isProfileImageUploading && (
              <div className="absolute bottom-0 left-0 right-0 top-0 bg-gray-50 opacity-75 dark:bg-gray-800"></div>
            )}
          </>
        ) : (
          <img
            src={emptyProfilePhoto}
            alt="Empty Profile Image"
           
            className="rounded-full object-cover"
          />
        )}
      </div>
      <input
        type="file"
        accept="image/*"
        ref={fileInputRef}
        className="hidden"
        onChange={handleFileChange}
      />
      <Button
        variant="outline"
        disabled={isProfileImageUploading}
        onClick={(e) => {
          e.stopPropagation();
          fileInputRef.current?.click();
        }}
      >
        <Camera className="h-5 w-5" />
        <span className="ml-2.5">Change Image</span>
      </Button>
    </div>
  );
};
