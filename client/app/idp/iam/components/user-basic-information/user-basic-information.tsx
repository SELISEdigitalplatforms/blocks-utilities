import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { checkValidDate, cn, formatFullDate } from "@/lib/utils";
import { UserCreationType } from "@blocks-idp/authentication/constants/authentication.constant";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";

interface ItemProps {
  label: string;
  children?: React.ReactNode;
  isLoading?: boolean;
}

const Item = ({ label, children, isLoading = false }: ItemProps) => (
  <div className="space-y-1.5">
    <p className="text-sm text-muted-foreground">{label}</p>
    {isLoading ? <Skeleton className="h-6 w-32" /> : <div className="text-base">{children}</div>}
  </div>
);

export const UserBasicInformation = ({
  id,
  projectKey,
  detailsGridClassName = "",
}: {
  id: string;
  projectKey: string;
  detailsGridClassName?: string;
}) => {
  const { isLoading, data } = useGetUserById({ id, projectKey });

  if (!isLoading && !data) return null;
  const { data: user } = data || { user: {} };

  return (
    <Card>
      <CardHeader>
        <CardTitle>Basic Information</CardTitle>
      </CardHeader>
      <CardContent>
        <div className={cn("grid grid-cols-1 gap-4 md:grid-cols-3 md:gap-y-[22px]", detailsGridClassName)}>
          <Item label="Name" isLoading={isLoading}>
            {user?.firstName} {user?.lastName}
          </Item>

          <Item label="Email" isLoading={isLoading}>
            <div className="flex items-center gap-2">
              {user?.email && <CopyToClipboardButton textToCopy={user?.email}>{user?.email}</CopyToClipboardButton>}
            </div>
          </Item>

          <Item label="No. of logins" isLoading={isLoading}>
            {user?.logInCount ?? "-"}
          </Item>

          <Item label="Status" isLoading={isLoading}>
            <Badge variant={user?.active ? "success" : "error"} className="w-fit rounded-sm py-1.5">
              {user?.active ? "Active" : " Inactive"}
            </Badge>
          </Item>
          <Item label="Latest login" isLoading={isLoading}>
            {user?.lastLoggedInTime && checkValidDate(user?.lastLoggedInTime)
              ? formatFullDate(new Date(user?.lastLoggedInTime))
              : "-"}
          </Item>

          <Item label="Signed up" isLoading={isLoading}>
            {user?.userCreationType && UserCreationType[user?.userCreationType] ? (
              <Badge variant="info" className="w-fit">
                {UserCreationType[user?.userCreationType]}
              </Badge>
            ) : (
              ""
            )}
          </Item>
        </div>
      </CardContent>
    </Card>
  );
};
