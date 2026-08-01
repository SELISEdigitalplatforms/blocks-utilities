import { Link } from "react-router";
import { motion } from "framer-motion";
import { Layers, BookOpenText } from "lucide-react";

export default function ConsoleCreateProject() {
  return (
    <motion.div
      initial={{ opacity: 0, y: 14 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.5, ease: [0.22, 1, 0.36, 1] }}
      className="relative overflow-hidden rounded-2xl bg-primary shadow-lg"
    >
      <div className="pointer-events-none absolute -right-16 -top-16 h-64 w-64 rounded-full bg-white/5" />
      <div className="pointer-events-none absolute -bottom-10 -right-4 h-40 w-40 rounded-full bg-white/5" />
      <div className="pointer-events-none absolute -left-10 bottom-0 h-32 w-32 rounded-full bg-white/[0.03]" />
      <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(hsl(210_100%_100%/0.07)_1px,transparent_1px)] [background-size:22px_22px]" />
      <div className="relative flex flex-col items-center gap-5 px-8 py-14 text-center sm:px-12 sm:py-20">
        <p className="text-[10px] font-semibold uppercase tracking-[0.15em] text-primary-foreground/60">
          Blocks OS Platform
        </p>
        <div className="flex h-12 w-12 items-center justify-center rounded-xl border border-white/15 bg-white/10">
          <Layers className="h-6 w-6 text-primary-foreground" />
        </div>
        <h3 className="text-3xl font-bold tracking-tight text-primary-foreground sm:text-4xl">
          Welcome to SELISE Blocks
        </h3>
        <p className="max-w-md text-sm leading-relaxed text-primary-foreground/70">
          Explore and manage all your projects in one place. With SELISE Blocks, building and
          scaling applications has never been easier. Start by creating a project.
        </p>
        <div className="flex flex-wrap justify-center gap-3 pt-1">
          <Link
            to="/create-project"
            className="inline-flex items-center rounded-lg bg-white px-5 py-2.5 text-sm font-semibold text-primary shadow-sm hover:bg-white/90"
          >
            Create a project
          </Link>
          <a
            href="https://docs.seliseblocks.com/"
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center gap-2 rounded-lg bg-white/15 px-4 py-2.5 text-sm font-semibold text-white backdrop-blur-sm hover:bg-white/25"
          >
            <BookOpenText className="h-4 w-4" />
            View documentation
          </a>
        </div>
      </div>
    </motion.div>
  );
}
