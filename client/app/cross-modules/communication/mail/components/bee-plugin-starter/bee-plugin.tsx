

import React, { forwardRef, useEffect, useImperativeHandle, useMemo, useState } from "react";
import Bee from "@mailupinc/bee-plugin";
import { IBeeConfig, IMergeTag, ISpecialLink } from "@mailupinc/bee-plugin/dist/types/bee";
import { blankTemplate } from "@blocks-communication/mail/constants/email-template";

const BEE_PLUGIN_CONTAINER_ID = "bee-plugin-container";

interface IBeePluginProps {
  beeUID: string;
  mergeTags?: IMergeTag[];
  specialLinks?: ISpecialLink[];
  onBeeSave(data: { htmlFile: string; jsonFile: string }): void;
  onPreviewModeChange?: (isPreviewOn: boolean) => void;
  onBeeTemplateLoad?: (isLoaded: boolean) => void;
  jsonFile?: any;
}

const BeePlugin = forwardRef(function Inner(
  {
    beeUID,
    mergeTags = [],
    specialLinks = [],
    onBeeSave,
    onPreviewModeChange,
    onBeeTemplateLoad,
    jsonFile = blankTemplate,
  }: IBeePluginProps,
  ref,
) {
  const [bee, setBee] = useState<Bee | null>(null);
  const [isBeeStarted, setIsBeeStarted] = useState(false);
  const [isPreviewOn, setIsPreviewOn] = useState(false);

  const beeConfig: IBeeConfig = useMemo(
    () => ({
      uid: beeUID,
      container: BEE_PLUGIN_CONTAINER_ID,
      autosave: 30,
      language: "en-US",
      specialLinks,
      mergeTags,
      onSave: (jsonFile, htmlFile) => {
        if (isPreviewOn) {
          bee?.togglePreview();
        }
        setIsPreviewOn(false);
        onBeeSave({ jsonFile, htmlFile });
      },
      onAutoSave: (jsonFile) => {
        console.log(`${new Date().toISOString()} autosaving...,`, jsonFile);
      },
      onTogglePreview: (isPreviewOn) => {
        setIsPreviewOn(isPreviewOn);
        onPreviewModeChange?.(isPreviewOn);
      },
      onLoad: () => {
        console.log("*** [integration] loading a new template...");
        onBeeTemplateLoad?.(true);
      },
      onError: (errorMessage) => console.error("onError ", errorMessage),
    }),
    [
      beeUID,
      specialLinks,
      mergeTags,
      onBeeSave,
      onPreviewModeChange,
      onBeeTemplateLoad,
      isPreviewOn,
      bee,
    ],
  );

  useEffect(() => {
    const beeInstance = new Bee();
    const conf = {
      authUrl: "https://auth.getbee.io/apiauth",
      beePluginUrl: "https://app-rsrc.getbee.io/plugin/BeePlugin.js",
    };

    beeInstance
      .getToken("your-client-id", "your-client-secret", conf)
      .then(() => jsonFile)
      .then((template) => {
        beeInstance.start(beeConfig, template, "", { shared: false }).then(() => {
          setIsBeeStarted(true);
          setBee(beeInstance);
        });
      })
      .catch((error) => console.error("Error during initialization --> ", error));
  }, [beeConfig, jsonFile]);

  useImperativeHandle(ref, () => ({
    submit() {
      bee?.save();
    },
    preview() {
      setIsPreviewOn(!isPreviewOn);
      bee?.preview();
    },
  }));

  return (
    <>
      {isBeeStarted && <div id={BEE_PLUGIN_CONTAINER_ID} className="h-[calc(100vh-60px)] w-full" />}
    </>
  );
});

export default BeePlugin;
