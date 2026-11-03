import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { checkValidDate, cn, formatFullDate } from "@/lib/utils";
import { getUserCreationType } from "@/idp/authentication/constants/authentication.constant";
import { useGetMe, useGetUserById } from "@/idp/iam/hooks/use-user";
import { useT } from "@seliseblocks/genesis-os";
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
  own = false,
}: {
  id: string;
  projectKey: string;
  detailsGridClassName?: string;
  own?: boolean;
}) => {
  const { t } = useT("utilities.idp");
  const meQuery = useGetMe({ enabled: own });
  const byIdQuery = useGetUserById({ id, projectKey, enabled: !own });
  const { isLoading, data } = own ? meQuery : byIdQuery;
  if (!isLoading && !data) return null;
  const { data: user } = data || { user: {} };
  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("USER_BASIC_INFORMATION.BASIC_INFORMATION")}</CardTitle>
      </CardHeader>
      <CardContent>
        <div className={cn("grid grid-cols-1 gap-4 md:grid-cols-3 md:gap-y-[22px]", detailsGridClassName)}>
          <Item label={t("common:NAME")} isLoading={isLoading}>
            {user?.firstName} {user?.lastName}
          </Item>
          <Item label={t("common:EMAIL")} isLoading={isLoading}>
            <div className="flex items-center gap-2">
              {user?.email && <CopyToClipboardButton textToCopy={user?.email}>{user?.email}</CopyToClipboardButton>}
            </div>
          </Item>
          <Item label={t("USER_BASIC_INFORMATION.NO_OF_LOGINS")} isLoading={isLoading}>
            {user?.logInCount ?? "-"}
          </Item>
          <Item label={t("common:STATUS")} isLoading={isLoading}>
            <Badge variant={user?.active ? "success" : "error"} className="w-fit rounded-sm py-1.5">
              {user?.active ? t("USER_BASIC_INFORMATION.ACTIVE") : t("USER_BASIC_INFORMATION.INACTIVE")}
            </Badge>
          </Item>
          <Item label={t("USER_BASIC_INFORMATION.LATEST_LOGIN")} isLoading={isLoading}>
            {user?.lastLoggedInTime && checkValidDate(user?.lastLoggedInTime)
              ? formatFullDate(new Date(user?.lastLoggedInTime))
              : "-"}
          </Item>
          <Item label={t("USER_BASIC_INFORMATION.SIGNED_UP")} isLoading={isLoading}>
            {user?.userCreationType && getUserCreationType()[user?.userCreationType] ? (
              <Badge variant="info" className="w-fit">
                {getUserCreationType()[user?.userCreationType]}
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
