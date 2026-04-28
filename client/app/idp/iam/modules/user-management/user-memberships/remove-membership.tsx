
import { Button } from "@/components/ui-kits/button/button";
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useUpdateUser, useGetUserById } from "@blocks-idp/iam/hooks/use-user";
import { IMembership } from "@blocks-idp/iam/models/user";

type RemoveMembershipProps = {
    open: boolean;
    onOpenChange: (open: boolean) => void;
    membership: IMembership;
    organizationName: string;
    userId: string;
    projectKey: string;
    onSuccess?: () => void;
};

export const RemoveMembership = ({
    open,
    onOpenChange,
    membership,
    organizationName,
    userId,
    projectKey,
    onSuccess,
}: RemoveMembershipProps) => {
    const { data: userData } = useGetUserById({ id: userId, projectKey });
    const { mutateAsync, isPending } = useUpdateUser({ id: userId, projectKey });

    const existingMemberships = userData?.data?.memberships || [];

    const onConfirm = async () => {
        try {
            const updatedMemberships = existingMemberships.filter(
                (m) => m.organizationId !== membership.organizationId
            );

            const res = await mutateAsync({
                ...userData?.data,
                memberships: updatedMemberships,
                itemId: userId,
                projectKey,
            });

            if (!res.isSuccess) {
                showErrorToast({ errors: res.errors });
                return;
            }

            showSuccessToast({ description: "Organization membership removed successfully" });
            onOpenChange(false);
            onSuccess?.();
        } catch (error) {
            if (isErrorWithErrors(error)) {
                showErrorToast({ errors: error.errors });
            } else {
                showErrorToast({ errors: "Something went wrong" });
            }
        }
    };

    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent className="sm:max-w-[425px]">
                <DialogHeader>
                    <DialogTitle>Remove organization membership</DialogTitle>
                    <DialogDescription>
                        Are you sure you want to remove the user from &quot;{organizationName}&quot;? This will
                        revoke all roles associated with this organization.
                    </DialogDescription>
                </DialogHeader>

                <DialogFooter>
                    <Button variant="outline" onClick={() => onOpenChange(false)}>
                        Cancel
                    </Button>
                    <Button variant="destructive" onClick={onConfirm} disabled={isPending}>
                        {isPending ? "Removing..." : "Remove"}
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
};
