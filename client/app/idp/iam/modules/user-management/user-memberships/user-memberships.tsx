
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { UserMembershipsList } from "./user-memberships-list";
import { AssignOrganization } from "./assign-organization";
import { useMemo } from "react";

type UserMembershipsProps = {
    id: string;
    projectKey: string;
};

export const UserMemberships = ({ id, projectKey }: UserMembershipsProps) => {
    const { data: userData, isLoading: isUserLoading } = useGetUserById({ id, projectKey });
    const { data: orgsData, isLoading: isOrgsLoading } = useGetOrganizations({
        projectKey,
        page: 0,
        pageSize: 1000,
    });

    const memberships = userData?.data?.memberships || [];

    // Create a map of organizationId to organizationName
    const orgNameMap = useMemo(() => {
        const map = new Map<string, string>();
        const organizations = orgsData?.organizations || [];
        organizations.forEach((org) => {
            map.set(org.itemId, org.name);
        });
        return map;
    }, [orgsData?.organizations]);

    const isLoading = isUserLoading || isOrgsLoading;

    return (
        <Card>
            <CardHeader className="flex flex-row items-center justify-between">
                <CardTitle>Organization</CardTitle>
                <AssignOrganization userId={id} projectKey={projectKey} />
            </CardHeader>
            <CardContent>
                <UserMembershipsList
                    memberships={memberships}
                    orgNameMap={orgNameMap}
                    isLoading={isLoading}
                    userId={id}
                    projectKey={projectKey}
                />
            </CardContent>
        </Card>
    );
};
