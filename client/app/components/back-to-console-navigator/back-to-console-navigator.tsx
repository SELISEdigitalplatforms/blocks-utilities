import { Button } from "@/components/ui-kits/button/button";
import { Link } from "react-router";
import { useT } from "@seliseblocks/genesis-os";

export function BackToConsoleNavigator() {
  const { t } = useT("utilities.shared");
  return (
    <Link to="/console">
      <Button variant="outline" className="bg-transparent hover:bg-slate-200 dark:hover:bg-gray-800">
        <div className="hidden flex-row items-center md:flex">{t("BACK_TO_CONSOLE_NAVIGATOR.BACK_TO_CONSOLE")}</div>
        <div className="flex flex-row items-center text-xs md:hidden">{t("BACK_TO_CONSOLE_NAVIGATOR.CONSOLE")}</div>
      </Button>
    </Link>
  );
}
