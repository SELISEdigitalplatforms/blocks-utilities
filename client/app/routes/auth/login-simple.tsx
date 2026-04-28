import { useEffect, useMemo, useState } from "react";
import { motion } from "framer-motion";
import { Button } from "@/components/ui-kits/button/button";
import { Logo } from "@/components/logo";
import { getRuntimeEnv } from "@/lib/runtime-env";
import {
  ShieldCheck,
  Users,
  KeyRound,
  Puzzle,
  BookOpenText,
  BrainCircuit,
  Activity,
  ArrowRight,
  MoveRight,
  Github,
} from "lucide-react";
import { Link } from "react-router-dom";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";

const pillars = [
  { icon: ShieldCheck, label: "Identity & MFA", desc: "Passkeys, OAuth 2.0, OIDC" },
  { icon: Users, label: "User Management", desc: "Roles, teams & access control" },
  { icon: BrainCircuit, label: "AI Agents", desc: "Embedded intelligence" },
  { icon: Activity, label: "Observability", desc: "Logs, metrics & tracing" },
  { icon: KeyRound, label: "Secrets & Auth", desc: "Secure credential vaults" },
  { icon: Puzzle, label: "Modular SDKs", desc: "Plug-in what you need" },
];

const ResourcesPanel = () => {
  const constructUrl = getRuntimeEnv("BLOCKS_CONSTRUCT_URL") || "https://construct.seliseblocks.com";

  const sdks = [
    {
      icon: "/assets/images/react-icon.png",
      name: "React",
      available: true,
      links: [
        { label: "npm", to: "https://www.npmjs.com/package/@seliseblocks/cli" },
        { label: "GitHub", to: "https://github.com/SELISEdigitalplatforms/l3-react-blocks-construct" },
        { label: "Demo", to: constructUrl },
      ],
    },
    { icon: "/assets/images/angular-icon.png", name: "Angular", available: false, links: [] },
    {
      icon: "/assets/images/dotnet-icon.png",
      name: ".NET",
      available: true,
      links: [
        { label: "NuGet", to: "https://www.nuget.org/profiles/SELISE" },
        { label: "GitHub", to: "https://github.com/SELISEdigitalplatforms/l0-net-blocks-construct" },
        { label: "PyPI", to: "https://pypi.org/project/seliseblocks-lmt/" },
      ],
    },
    { icon: "/assets/images/ruby-icon.png", name: "Ruby", available: false, links: [] },
  ];

  return (
    <aside className="mt-8 w-full shrink-0 lg:mt-0 lg:w-[380px] xl:w-[420px]">
      <div className="overflow-hidden rounded-2xl border border-[hsl(var(--border-default))] bg-[hsl(var(--card))] shadow-md">
        
        <div className="relative overflow-hidden bg-primary px-6 py-6">
          <div className="absolute -right-8 -top-8 h-32 w-32 rounded-full bg-white/5" />
          <div className="absolute -bottom-6 -right-2 h-20 w-20 rounded-full bg-white/5" />
          <p className="relative text-[10px] font-semibold uppercase tracking-[0.15em] text-primary-foreground/60">
            Developer Resources
          </p>
          <h3 className="relative mt-1 text-xl font-bold text-primary-foreground">
            Build with Blocks
          </h3>
          <p className="relative mt-1.5 text-sm text-primary-foreground/70">
            Open-source SDKs and CLI tools to accelerate your integration.
          </p>
          <Link
            to="https://docs.seliseblocks.com/"
            target="_blank"
            className="relative mt-4 inline-flex items-center gap-2 rounded-lg bg-white/15 px-4 py-2 text-xs font-semibold text-white backdrop-blur-sm transition-colors hover:bg-white/25"
          >
            <BookOpenText className="h-3.5 w-3.5" />
            Read the Docs
            <ArrowRight className="h-3 w-3" />
          </Link>
        </div>

        
        <div className="divide-y divide-[hsl(var(--border-default))]">
          {sdks.map((sdk) => (
            <div key={sdk.name} className="flex items-center justify-between px-6 py-3.5">
              <div className="flex items-center gap-3">
                <div
                  className={`flex h-8 w-8 items-center justify-center rounded-lg border border-[hsl(var(--border-default))] bg-[hsl(var(--card))] shadow-sm ${!sdk.available ? "opacity-35" : ""}`}
                >
                  <img src={sdk.icon} width={18} height={18} alt={sdk.name} />
                </div>
                <span
                  className={`text-sm font-medium ${sdk.available ? "text-[hsl(var(--high-emphasis))]" : "text-[hsl(var(--low-emphasis))]"}`}
                >
                  {sdk.name}
                </span>
              </div>
              {sdk.available ? (
                <div className="flex items-center gap-2.5 text-xs">
                  {sdk.links.map((link, i) => (
                    <span key={link.label} className="flex items-center gap-2.5">
                      {i > 0 && <span className="h-3 w-px bg-[hsl(var(--border-default))]" />}
                      <Link to={link.to} target="_blank" className="font-medium text-primary hover:underline">
                        {link.label}
                      </Link>
                    </span>
                  ))}
                </div>
              ) : (
                <span className="rounded-full bg-[hsl(var(--neutral-50))] px-2.5 py-0.5 text-[11px] font-medium text-[hsl(var(--low-emphasis))]">
                  Coming soon
                </span>
              )}
            </div>
          ))}
        </div>

        
        <div className="flex items-center justify-between bg-[hsl(var(--surface-app))] px-6 py-3.5">
          <span className="text-xs text-[hsl(var(--medium-emphasis))]">Fully open source</span>
          <Link
            to="https://github.com/SELISEdigitalplatforms"
            target="_blank"
            className="inline-flex items-center gap-1.5 rounded-lg border border-[hsl(var(--border-default))] bg-[hsl(var(--card))] px-3 py-1.5 text-xs font-medium text-[hsl(var(--high-emphasis))] shadow-sm transition-all hover:border-primary/40 hover:shadow-md"
          >
            <Github className="h-4 w-4" />
            View on GitHub
          </Link>
        </div>
      </div>
    </aside>
  );
};

export default function LoginSimplePage() {
  const [isLoading, setIsLoading] = useState(false);
  const [titleNumber, setTitleNumber] = useState(0);
  const titles = useMemo(
    () => ["observable", "intelligent", "scalable", "resilient", "secure"],
    [],
  );

  useEffect(() => {
    const timeoutId = setTimeout(() => {
      setTitleNumber((prev) => (prev === titles.length - 1 ? 0 : prev + 1));
    }, 2400);
    return () => clearTimeout(timeoutId);
  }, [titleNumber, titles]);

  const handleLogin = () => {
    setIsLoading(true);
    const blocksKey = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY");

    const params = new URLSearchParams({
      response_type: "code",
      client_id: "44ce2f9b-0ca4-4ad8-b8d4-bb775b61d68e",
      redirect_uri: "https://dev-os.blocksdevelopers.com/oidc",
      scope: "openId",
      audience: "https://dev-os.blocksdevelopers.com",
      state: "039849038",
      nonce: "35443",
      ...(blocksKey ? { "x-blocks-key": blocksKey } : {}),
    });

    window.location.href = `https://dev-idp.blocksdevelopers.com/api/Authentication/Authorize?${params.toString()}`;
  };

  return (
    <div className="relative flex min-h-screen flex-col bg-[hsl(var(--surface-app))]">
      
      <header className="relative z-10 flex items-center px-6 py-5 xl:px-[154px]">
        <Logo width={120} height={52} />
        <div className="absolute right-6 top-5 xl:right-[154px]">
          <ModeToggle />
        </div>
      </header>

      
      <main className="relative z-10 flex flex-1 flex-col items-start justify-center gap-16 px-6 py-12 lg:flex-row lg:items-center lg:gap-16 lg:py-0 xl:px-[154px]">
        
        <div className="flex flex-1 flex-col items-start gap-6">
          
          <div className="flex flex-col gap-2">
            <p className="text-sm font-semibold uppercase tracking-[0.1em] text-primary">Blocks OS Platform</p>
            <h1 className="max-w-xl text-5xl font-semibold tracking-tight text-[hsl(var(--high-emphasis))] lg:text-6xl">
              Backends that are
            </h1>
            <div className="relative flex h-[80px] overflow-visible lg:h-[88px]">
              {titles.map((title, index) => (
                <motion.span
                  key={index}
                  className="absolute text-5xl font-semibold tracking-tight text-primary lg:text-6xl"
                  initial={{ opacity: 0, y: 28, filter: "blur(6px)" }}
                  transition={{ duration: 0.75, ease: [0.22, 1, 0.36, 1] }}
                  animate={
                    titleNumber === index
                      ? { y: 0, opacity: 1, filter: "blur(0px)" }
                      : { y: titleNumber > index ? -28 : 28, opacity: 0, filter: "blur(6px)" }
                  }
                >
                  {title}.
                </motion.span>
              ))}
            </div>
          </div>

          <p className="max-w-lg text-lg leading-relaxed tracking-tight text-muted-foreground">
            Blocks OS is a modern platform for building and deploying secure, scalable applications with built-in observability, AI
            capabilities, and comprehensive identity management. Focus on your application logic
            while Blocks OS handles the infrastructure.
          </p>

          
          <div className="flex flex-wrap gap-2">
            {pillars.map(({ icon: Icon, label }) => (
              <div
                key={label}
                className="inline-flex items-center gap-1.5 rounded-full border border-[hsl(var(--border-default))] bg-[hsl(var(--card))] px-3 py-1.5 text-xs font-medium text-[hsl(var(--high-emphasis))] shadow-sm"
              >
                <Icon className="h-3.5 w-3.5 text-primary" />
                {label}
              </div>
            ))}
          </div>

          
          <div className="flex flex-col gap-2 pt-2">
            <div className="flex flex-row gap-3">
              <Button
                size="lg"
                className="group gap-2"
                disabled={isLoading}
                onClick={handleLogin}
              >
                {isLoading ? "Redirecting…" : "Log in to your account"}
                {!isLoading && (
                  <MoveRight className="h-4 w-4 transition-transform group-hover:translate-x-1" />
                )}
              </Button>
            </div>
          
          </div>
        </div>

        <ResourcesPanel />
      </main>
    </div>
  );
}

