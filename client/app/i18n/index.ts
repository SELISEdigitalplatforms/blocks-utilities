import { registerTranslations } from "@seliseblocks/genesis-os";

// One English file per module: i18n/<module>.en.json (module = file name before ".en.json").
const dictionaries = import.meta.glob<Record<string, string>>("./*.en.json", {
  eager: true,
  import: "default",
});

for (const [path, dictionary] of Object.entries(dictionaries)) {
  const module = /\.\/(.+)\.en\.json$/.exec(path)?.[1];
  if (module) registerTranslations(module, dictionary);
}
