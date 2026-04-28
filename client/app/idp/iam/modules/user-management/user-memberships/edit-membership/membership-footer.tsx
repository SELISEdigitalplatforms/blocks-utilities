import { SheetFooter } from "@/components/ui-kits/sheet/sheet";
import { Button } from "@/components/ui-kits/button/button";

type MembershipFooterProps = {
  isEditing: boolean;
  isPending: boolean;
  onCancel: () => void;
  onSave: () => void;
  onEdit: () => void;
  onUnassign: () => void;
};

export const MembershipFooter = ({
  isEditing,
  isPending,
  onCancel,
  onSave,
  onEdit,
  onUnassign,
}: MembershipFooterProps) => (
  <SheetFooter className="mt-auto flex gap-2">
    {isEditing ? (
      <>
        <Button variant="outline" onClick={onCancel} className="flex-1">
          Cancel
        </Button>
        <Button onClick={onSave} disabled={isPending} className="flex-1">
          {isPending ? "Saving..." : "Save"}
        </Button>
      </>
    ) : (
      <>
        <Button
          variant="outline"
          onClick={onUnassign}
          className="flex-1 border-destructive text-destructive hover:bg-destructive/10"
        >
          Unassign User
        </Button>
        <Button variant="outline" onClick={onEdit} className="flex-1">
          Edit
        </Button>
      </>
    )}
  </SheetFooter>
);
