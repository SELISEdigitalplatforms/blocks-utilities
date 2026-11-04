import { UserRound } from 'lucide-react'
import { Link } from 'react-router'
import { LogOutButton } from '@/components/auth/log-out-button'
import { Button } from '@/components/ui-kits/button/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui-kits/dropdown-menu/dropdown-menu'
import { useGetMe } from '@/idp/iam/hooks/use-user'
import { useT } from "@seliseblocks/genesis-os";
function UserDropdownMenuLogo() {
  const { t } = useT("utilities.shared");
  const { data } = useGetMe()
  const userData = data?.data || {
    firstName: '',
    lastName: '',
    profileImageUrl: '',
  }
  const initials =
    `${userData.firstName?.[0] || ''}${userData.lastName?.[0] || ''}`.toUpperCase()
  if (userData.profileImageUrl) {
    return (
      <img
        src={userData.profileImageUrl}
        alt={t("USER_DROPDOWN_MENU.PROFILE")}
        className='h-full w-full object-cover'
      />
    )
  }
  if (initials) {
    return <span>{initials}</span>
  }
  return <UserRound className='h-4 w-4' />
}
export function UserDropdownMenu() {
  const { t } = useT("utilities.shared");
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant='link'
          size='icon'
          className='relative h-10 w-10 overflow-hidden rounded-full bg-[hsl(var(--avatar-surface-default))] p-0 text-base font-normal text-[hsl(var(--avatar-text-high-emphasis))] hover:no-underline'
        >
          <UserDropdownMenuLogo />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align='end' className='w-56'>
        <DropdownMenuGroup>
          <DropdownMenuItem asChild>
            <Link to='/profile'>{t("USER_DROPDOWN_MENU.MY_PROFILE")}</Link>
          </DropdownMenuItem>
          <DropdownMenuItem disabled>{t("USER_DROPDOWN_MENU.PRIVACY")}</DropdownMenuItem>
          <DropdownMenuItem disabled>{t("USER_DROPDOWN_MENU.SUPPORT")}</DropdownMenuItem>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuGroup>
          <DropdownMenuItem>
            <LogOutButton />
          </DropdownMenuItem>
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
