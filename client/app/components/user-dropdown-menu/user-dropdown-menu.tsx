import { UserRound } from "lucide-react";
import { Link } from "react-router-dom";
import { LogOutButton } from "@/components/auth/log-out-button";
import { Button } from "@/components/ui-kits/button/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { useGetUser } from "@/idp/iam/hooks/use-user";

function UserDropdownMenuLogo() {
  const { data } = useGetUser({ enabled: true });
  const userData = data?.data || { firstName: "", lastName: "", profileImageUrl: "" };
  const initials = `${userData.firstName?.[0] || ""}${userData.lastName?.[0] || ""}`.toUpperCase();

  if (userData.profileImageUrl) {
    return (
      <img
        src={userData.profileImageUrl}
        alt="Profile"
        className="h-full w-full object-cover"
      />
    );
  }

  if (initials) {
    return <span>{initials}</span>;
  }

  return <UserRound className="h-4 w-4" />;
}

export function UserDropdownMenu() {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="link"
          size="icon"
          className="relative h-10 w-10 overflow-hidden rounded-full bg-[hsl(var(--avatar-surface-default))] p-0 text-base font-normal text-[hsl(var(--avatar-text-high-emphasis))] hover:no-underline"
        >
          <UserDropdownMenuLogo />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        <DropdownMenuGroup>
          <DropdownMenuItem asChild>
            <Link to="/profile">My profile</Link>
          </DropdownMenuItem>
          <DropdownMenuItem disabled>Privacy</DropdownMenuItem>
          <DropdownMenuItem disabled>Support</DropdownMenuItem>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuGroup>
          <DropdownMenuItem>
            <LogOutButton />
          </DropdownMenuItem>
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}