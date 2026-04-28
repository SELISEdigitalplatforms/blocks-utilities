import { Button } from "@/components/ui-kits/button/button";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { BookOpenText } from "lucide-react";
// import { BookOpenText, Component } from "lucide-react";
import { Link } from "react-router-dom";

export const BlockInfo = () => {
  return (
    <div className="mt-[24px] w-full p-4 shadow-none md:p-0 lg:mt-0 lg:max-w-md">
      <div className="mb-[36px] flex items-center gap-4">
        <Link to="https://docs.seliseblocks.com/" target="_blank" className="w-full">
          <Button variant="outline" className="w-full">
            <BookOpenText className="mr-3 h-4 w-4" />
            See Docs
          </Button>
        </Link>
      </div>
      <div className="mb-[36px] flex flex-col gap-4">
        <h2 className="text-xl font-semibold">Frontend</h2>
        <div className="flex items-center gap-4">
          <div className="flex w-[50%] flex-col gap-2">
            <Button variant="outline" className="w-full">
              <img
                src="/assets/images/react-icon.png"
                width={20}
                height={20}
                alt="Reactjs Logo"
              />
            </Button>
            <div className="flex items-center gap-2 text-blue-700 md:justify-between">
              <Link
                to="https://www.npmjs.com/package/@seliseblocks/cli"
                className="text-primary"
                target="_blank"
              >
                Npm
              </Link>
              <span className="h-4 w-[1px] bg-gray-300"></span>
              <Link
                to="https://github.com/SELISEdigitalplatforms/l3-react-blocks-construct"
                className="text-primary"
                target="_blank"
              >
                GitHub
              </Link>
              <span className="h-4 w-[1px] bg-gray-300"></span>
              <Link
                to={getRuntimeEnv("BLOCKS_CONSTRUCT_URL") || "https://construct.seliseblocks.com"}
                className="text-primary"
                target="_blank"
              >
                Demo
              </Link>
            </div>
          </div>
          <div className="flex w-[50%] flex-col gap-2">
            <Button variant="outline" className="w-full" disabled>
              <img
                src="/assets/images/angular-icon.png"
                width={20}
                height={20}
                alt="Angular Logo"
              />
            </Button>
            <p className="text-medium-emphasis">Coming soon</p>
          </div>
        </div>
      </div>
      <div className="mb-[36px] flex flex-col gap-4">
        <h2 className="text-xl font-semibold">Backend</h2>
        <div className="flex items-center gap-4">
          <div className="flex w-[50%] flex-col gap-2">
            <Button variant="outline" className="w-full">
              <img
                src="/assets/images/dotnet-icon.png"
                width={20}
                height={20}
                alt="DotNet Logo"
              />
            </Button>
            <div className="flex items-center gap-1 text-blue-700 md:justify-between">
              <Link
                to="https://www.nuget.org/profiles/SELISE"
                className="text-primary"
                target="_blank"
              >
                NuGet
              </Link>
              <span className="h-4 w-[0.5px] bg-gray-300"></span>
              <Link
                to="https://github.com/SELISEdigitalplatforms/l0-net-blocks-construct"
                className="text-primary"
                target="_blank"
              >
                GitHub
              </Link>
              <span className="h-4 w-[0.5px] bg-gray-300"></span>
              <Link
                to="https://pypi.org/project/seliseblocks-lmt/"
                className="text-primary"
                target="_blank"
              >
                PyPI
              </Link>
            </div>
            {/* <div className="flex items-center gap-2 text-blue-700 md:justify-between">
              <Link
                to="https://github.com/SELISEdigitalplatforms/l0-net-blocks-construct"
                className="text-primary"
                target="_blank"
              >
                GitHub
              </Link>
            </div> */}
          </div>
          <div className="flex w-[50%] flex-col gap-2">
            <Button variant="outline" className="w-full" disabled>
              <img src="/assets/images/ruby-icon.png" width={20} height={20} alt="RUby Logo" />
            </Button>
            <p className="text-medium-emphasis">Coming soon</p>
          </div>
        </div>
      </div>
      <div className="flex flex-col gap-4">
        <p className="text-center">Fully open source.</p>
        <Link to="https://github.com/SELISEdigitalplatforms" target="_blank">
          <Button variant="outline" className="w-full">
            <img
              src="/assets/images/social-media-github.png"
              width={20}
              height={20}
              className="mr-3"
              alt="github Logo"
            />
            Open in GitHub
          </Button>
        </Link>
      </div>
    </div>
  );
};
