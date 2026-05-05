

import React from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";

const templateData = [
  {
    id: "1",
    name: "Simple Text",
    thumbnailUrl: "/assets/images/services/email/email-template-sample-1.png",
    description: "This is the first template.",
  },
  {
    id: "2",
    name: "1:2:1:2 Column",
    thumbnailUrl: "/assets/images/services/email/email-template-sample-2.png",
    description: "This is the second template.",
  },
  {
    id: "3",
    name: "1:2 Column",
    thumbnailUrl: "/assets/images/services/email/email-template-sample-2.png",
    description: "This is the third template.",
  },
  {
    id: "4",
    name: "1:2 Column • Alternate",
    thumbnailUrl: "/assets/images/services/email/email-template-sample-2.png",
    description: "This is the third template.",
  },
  {
    id: "5",
    name: "1:2 Column • Alternate • 2",
    thumbnailUrl: "/assets/images/services/email/email-template-sample-2.png",
    description: "This is the third template.",
  },
  {
    id: "6",
    name: "1:2 Column • Alternate • 3",
    thumbnailUrl: "/assets/images/services/email/email-template-sample-2.png",
    description: "This is the third template.",
  },
  {
    id: "7",
    name: "1:2 Column • Alternate • 4",
    thumbnailUrl: "/assets/images/services/email/email-template-sample-2.png",
    description: "This is the third template.",
  },
  {
    id: "8",
    name: "1:2 Column • Alternate • 5",
    thumbnailUrl: "/assets/images/services/email/email-template-sample-2.png",
    description: "This is the third template.",
  },
  {
    id: "9",
    name: "1:2 Column • Alternate • 6",
    thumbnailUrl: "/assets/images/services/email/email-template-sample-2.png",
    description: "This is the third template.",
  },
  {
    id: "10",
    name: "1:2 Column • Alternate • 7",
    thumbnailUrl: "/assets/images/services/email/email-template-sample-2.png",
    description: "This is the third template.",
  },
];

const EmailTemplates = () => {
  return (
    <div>
      <div className="hidden md:flex">
        <PageBreadcrumb breadcrumbIndex={2} />
      </div>
      <div className="mt-5">
        <h1 className="text-lg font-semibold md:text-2xl">Email Templates</h1>
      </div>
      <Tabs defaultValue="designed" className="mt-6">
        <div className="mb-5 flex items-center text-base">
          <TabsList className="h-[42px] bg-blocks-primary-shades-300">
            <TabsTrigger value="designed" className="h-8">
              Pre-designed
            </TabsTrigger>
            <TabsTrigger value="myTemplates" className="h-8">
              My Templates
            </TabsTrigger>
          </TabsList>
          <div className="ml-auto flex items-center gap-2">
            <Button
              size="sm"
              variant="default"
              className="h-10 bg-primary text-sm text-primary-foreground"
            >
              <Plus className="h-5 w-5 md:mr-2" />
              <span className="sr-only sm:not-sr-only">Add Template</span>
            </Button>
          </div>
        </div>
        <TabsContent value="designed">
          <div className="rounded-sm border bg-background p-4">
            <h1 className="text-xl font-semibold">Templates</h1>
            <div className="mt-5 grid grid-cols-1 gap-5 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
              {templateData.map((item) => (
                <div key={item.id} className="flex h-full flex-col justify-center gap-2">
                  <div className="relative flex h-[360px] flex-col items-center justify-center rounded-sm border">
                    <img src={item.thumbnailUrl} alt="email-template" width={240} height={360} />
                  </div>
                  <p className="mt-3 text-base">{item.name}</p>
                </div>
              ))}
            </div>
          </div>
        </TabsContent>
        <TabsContent value="myTemplates">
          <div className="rounded-sm border bg-background p-4">
            <h1 className="text-xl font-semibold">Templates</h1>
            <div className="mt-5 grid grid-cols-1 gap-5 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
              {templateData.map((item) => (
                <div key={item.id} className="flex h-full flex-col justify-center gap-2">
                  <div className="relative flex h-[360px] flex-col items-center justify-center rounded-sm border">
                    <img src={item.thumbnailUrl} alt="email-template" width={240} height={360} />
                  </div>
                  <p className="mt-3 text-base">{item.name}</p>
                </div>
              ))}
            </div>
          </div>
        </TabsContent>
      </Tabs>
    </div>
  );
};

export default EmailTemplates;
