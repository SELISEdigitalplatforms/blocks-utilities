import { useContext } from "react";
import { userMfaContext } from "../../user-mfa";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";

const imageUrl = "/assets/images/mail-sent.png";

export const UserMfaVerifyGuideLineEmail = () => {
  const { userId, projectKey } = useContext(userMfaContext);
  const { data } = useGetUserById({ id: userId, projectKey });

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-center">
        <img src={imageUrl} alt="email sent" width="120" height="100" />
      </div>
      <div className="mt-6">
        <h3 className="text-2xl font-bold">Email sent</h3>
        <p className="mt-2">
          We’ve sent a verification key to your registered email address ({data?.data.email}).{" "}
        </p>
      </div>
      <p>Did not receive mail? </p>
      <p>Please enter the key below to complete your setup. </p>
    </div>
  );
};
