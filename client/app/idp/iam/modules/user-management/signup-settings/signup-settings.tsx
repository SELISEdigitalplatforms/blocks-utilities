

import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { 
  Dialog, 
  DialogContent, 
  DialogDescription, 
  DialogHeader, 
  DialogTitle, 
  DialogTrigger,
  DialogFooter,
  DialogClose
} from "@/components/ui-kits/dialog/dialog";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Wrench } from "lucide-react";
import { useGetSignUpSetting, useSaveSignUpSetting } from "@blocks-idp/iam/hooks/use-user";
import { useProjectStore } from "@/store/useProjectStore";

export const SignupSettings = () => {
  const [open, setOpen] = useState(false);
  const [allowSignup, setAllowSignup] = useState(false);
  const [emailPassword, setEmailPassword] = useState(false);
  const [sso, setSso] = useState(false);
  const initializedRef = useRef(false);

  const tenantId = useProjectStore().selectedProject?.tenantId || "";

  const { data: signUpSettingData } = useGetSignUpSetting({
    projectKey: tenantId
  }, {
    enabled: !!tenantId,
  });

  const { mutateAsync: saveSignUpSetting, isPending } = useSaveSignUpSetting();

  useEffect(() => {
    if (signUpSettingData && !initializedRef.current) {
      initializedRef.current = true;
      const ep = signUpSettingData.isEmailPasswordSignUpEnabled;
      const ssoEnabled = signUpSettingData.isSSoSignUpEnabled;
      setEmailPassword(ep);
      setSso(ssoEnabled);
      setAllowSignup(ep || ssoEnabled);
    }
  }, [signUpSettingData]);

  const handleAllowSignupChange = (checked: boolean) => {
    setAllowSignup(checked);
    if (!checked) {
      setEmailPassword(false);
      setSso(false);
    }
  };

  const isSaveDisabled = isPending || (allowSignup && !emailPassword && !sso);

  const submitHandler = async () => {
    await saveSignUpSetting({
      isEmailPasswordSignUpEnabled: allowSignup && emailPassword,
      isSSoSignUpEnabled: allowSignup && sso,
      projectKey: tenantId,
      itemId: signUpSettingData?.itemId || "",
    });
    setOpen(false);
  };

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button variant="outline">
            <Wrench className="mr-2 aspect-square w-4" />
            <span>Signup Settings</span>
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Signup Settings</DialogTitle>
          <DialogDescription>
            Configure signup settings for users.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4 py-4">
          <div className="flex items-center space-x-2">
            <Checkbox 
              id="allow-signup" 
              checked={allowSignup} 
              onCheckedChange={(checked) => handleAllowSignupChange(!!checked)} 
            />
            <label
              htmlFor="allow-signup"
              className="text-sm font-medium leading-none cursor-pointer"
            >
              Allow signup
            </label>
          </div>
          
          
            <div className="ml-6 flex flex-col gap-3">
              <div className="flex items-center space-x-2">
                <Checkbox 
                  id="email-password" 
                  checked={emailPassword} 
                  onCheckedChange={(checked) => setEmailPassword(!!checked)} 
                  disabled={!allowSignup}
                />
                <label
                  htmlFor="email-password"
                  className={`text-sm font-medium leading-none ${
                    allowSignup ? "cursor-pointer" : "text-muted-foreground cursor-not-allowed"
                  }`}
                >
                  Email and password
                </label>
              </div>
              <div className="flex items-center space-x-2">
                <Checkbox 
                  id="sso" 
                  checked={sso} 
                  onCheckedChange={(checked) => setSso(!!checked)} 
                  disabled={!allowSignup}
                />
                <label
                  htmlFor="sso"
                  className={`text-sm font-medium leading-none ${
                    allowSignup ? "cursor-pointer" : "text-muted-foreground cursor-not-allowed"
                  }`}
                >
                  SSO
                </label>
              </div>
            </div>
          
        </div>

        <DialogFooter>
          <DialogClose asChild>
            <Button variant="outline">Cancel</Button>
          </DialogClose>
          <Button onClick={submitHandler} disabled={isSaveDisabled}>
            {isPending ? "Saving..." : "Save"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};