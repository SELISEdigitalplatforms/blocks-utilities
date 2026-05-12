import { ArrowUpRight } from "lucide-react";
import { motion } from "framer-motion";

type DocCardProps = {
  label: string;
  imageUri: string;
  description: string;
  url: string;
};

const DocCard = ({ label, imageUri, description, url }: DocCardProps) => {
  return (
    <motion.a
      href={url}
      target="_blank"
      rel="noopener noreferrer"
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      whileHover={{ y: -4 }}
      transition={{ duration: 0.35, ease: [0.22, 1, 0.36, 1] }}
      className="group flex flex-col overflow-hidden rounded-2xl border border-[hsl(var(--border-default))] bg-[hsl(var(--card))] shadow-sm transition-all duration-300 hover:border-primary/40 hover:shadow-xl hover:shadow-primary/5"
    >
      <div className="relative flex items-center justify-center overflow-hidden bg-[hsl(var(--surface-app))] px-8 py-10">
        <div className="absolute inset-0 bg-[radial-gradient(hsl(var(--border-default))_1px,transparent_1px)] [background-size:18px_18px] opacity-60" />
        <img
          src={imageUri}
          width={148}
          height={148}
          alt={label}
          className="relative object-contain drop-shadow-sm transition-transform duration-500 group-hover:scale-105"
        />
      </div>

      <div className="flex flex-1 flex-col gap-2 p-5">
        <div className="flex items-start justify-between gap-2">
          <h4 className="text-base font-semibold text-[hsl(var(--high-emphasis))]">{label}</h4>
          <ArrowUpRight className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground transition-all duration-200 group-hover:-translate-y-0.5 group-hover:translate-x-0.5 group-hover:text-primary" />
        </div>
        <p className="text-sm leading-relaxed text-muted-foreground">{description}</p>
        <span className="mt-auto pt-3 text-xs font-medium text-primary opacity-0 transition-opacity duration-200 group-hover:opacity-100">
          Explore →
        </span>
      </div>
    </motion.a>
  );
};

const data = [
  {
    label: "Docs",
    description:
      "Established standards that help project managers and technical leaders minimize project risks.",
    imageUri: "/assets/images/console/console_timeline.png",
    url: "https://github.com/SELISEdigitalplatforms/Wiki-BlocksGuideline-Code/wiki",
  },
  {
    label: "Code",
    description:
      "A repository of well-documented, reusable, tried and tested core components for developers.",
    imageUri: "/assets/images/console/console_coding.png",
    url: "https://github.com/SELISEdigitalplatforms",
  },
  {
    label: "Cloud",
    description: "High-performing, optimized, and 24/7 monitored enterprise cloud deployment.",
    imageUri: "/assets/images/console/console_data-center.png",
    url: "https://selisegroup.com/blocks/",
  },
];

export const DefaultDoc = () => {
  return (
    <div className="grid gap-5 md:grid-cols-2 lg:grid-cols-3">
      {data.map((item, index) => (
        <DocCard key={index} {...item} />
      ))}
    </div>
  );
};
