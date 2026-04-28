import { Label } from "@/components/ui-kits/label/label";
import { RadioGroup, RadioGroupItem } from "@/components/ui-kits/radio-group/radio-group";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useGetMFAConfig } from "@blocks-idp/mfa/hooks/use-mfa-config";
import { MFA_Provider_Data } from "@blocks-idp/mfa/utils/mfa-config";
import { useMemo } from "react";

type UserMFAMethodListProps = {
  selected: number;
  setSelected: (selected: number) => void;
  projectKey: string;
};

export const UserMFAMethodList = ({
  selected,
  setSelected,
  projectKey,
}: UserMFAMethodListProps) => {
  const { isLoading, isFetching, data } = useGetMFAConfig({ projectKey });
  const availableMFaMethod = useMemo(() => {
    if (!data?.userMfaType.length) return [];
    return MFA_Provider_Data.filter((item) => data?.userMfaType.includes(item.type));
  }, [data?.userMfaType]);
  return (
    <>
      {isLoading || isFetching ? (
        <div>
          <Skeleton className="h-5 w-1/2" />
          <Skeleton className="mt-2 h-5 w-full" />
          <Skeleton className="mt-2 h-5 w-full" />
        </div>
      ) : (
        <RadioGroup
          value={selected.toString()}
          onValueChange={(val) => setSelected(Number(val))}
          className="gap-5"
        >
          {availableMFaMethod.map((item) => (
            <div className="flex items-center gap-2" key={item.type}>
              <RadioGroupItem value={item.type.toString()} id={item.type.toString()} />
              <Label htmlFor={item.type.toString()} className="cursor-pointer text-sm font-medium">
                {item.label}
              </Label>
            </div>
          ))}
        </RadioGroup>
      )}
    </>
  );
};
