import { tStatic } from "@seliseblocks/genesis-os";

export interface BlocksProduct {
  /** Unique slug identifier, e.g. "blocks-iam", "blocks-os" */
  name: string;
  /** Display name, e.g. "blocks IAM" */
  appName: string;
  /** Short badge label shown on the carousel card */
  badge: string;
  /** One-line tagline */
  tagline: string;
  /** Short animated prefix for the hero keyword line */
  descriptionTitle: string;
  /** Animated cycling words for the hero keyword line */
  keywords: string[];
  /** Carousel card description (keep concise) */
  shortDescription: string;
  /** Full-length description for detail views */
  description: string;
  /** Feature chip labels */
  featureChips: string[];
  /** Link target for the CTA button */
  url: string;
  /** CTA button label, e.g. "Visit blocks IAM" */
  cta: string;
}

export const getBlocksProducts = (): BlocksProduct[] => [
  {
    name: "blocks-os",
    appName: "blocks OS",
    badge: "Platform Core",
    tagline: "Enterprise application operating system.",
    descriptionTitle: "Your application control plane",
    keywords: ["secure", "isolated", "auditable"],
    shortDescription:
      "A secure plane for your application environment, secrets, and sensitive configuration.",
    description:
      tStatic("utilities.shared", "BLOCKS_PRODUCTS.BLOCKS_OS_IS_A_SECURE"),
    featureChips: [
      "Multi-tenant isolation",
      "Scoped secrets",
      "Audit trails",
      "Log & trace handling",
      "Role-based access",
    ],
    url: "",
    cta: "Visit blocks OS",
  },
  {
    name: "blocks-localization",
    appName: "blocks Localization",
    badge: "i18n",
    tagline: "Localization infrastructure for modern teams.",
    descriptionTitle: "Translate without the overhead",
    keywords: [
      "multilingual",
      "contextual",
      "automated",
      "instant",
      "scalable",
    ],
    shortDescription:
      "Manage multilingual interfaces with AI translation, bulk key operations, and a real-time browser extension behind secure RBAC.",
    description:
      tStatic("utilities.shared", "BLOCKS_PRODUCTS.BLOCKS_LOCALIZATION_IS_A_KEY"),
    featureChips: [
      "Browser extension",
      "Bulk key operations",
      "Context-aware translations",
      "Real-time publishing",
    ],
    url: "",
    cta: "Visit blocks Localization",
  },
  {
    name: "blocks-iam",
    appName: "blocks IAM",
    badge: "Identity",
    tagline: "Enterprise identity, handled end-to-end.",
    descriptionTitle: "One identity layer for every app",
    keywords: ["secure", "unified", "seamless"],
    shortDescription:
      "Unified identity and access management with SSO, MFA, role-based controls, and external IdP integration.",
    description:
      tStatic("utilities.shared", "BLOCKS_PRODUCTS.BLOCKS_IAM_IS_THE_UNIFIED"),
    featureChips: [
      "SSO & IdP integration",
      "Multi-factor auth",
      "Role-based access",
      "CAPTCHA & client credentials",
      "Magic links",
    ],
    url: "",
    cta: "Visit blocks IAM",
  },
  {
    name: "blocks-data",
    appName: "blocks Data",
    badge: "Secure Storage",
    tagline: "Instant APIs over any data operations.",
    descriptionTitle: "Your data, API-ready",
    keywords: [
      "queryable",
      "structured",
      "validated",
      "versioned",
      "accessible",
    ],
    shortDescription:
      "Instant GraphQL APIs over managed or BYO databases with visual schema design, access control, and multi-provider object storage.",
    description:
      tStatic("utilities.shared", "BLOCKS_PRODUCTS.BLOCKS_DATA_GIVES_YOU_INSTANT"),
    featureChips: [
      "GraphQL APIs",
      "Column-level access",
      "CRUD playground",
      "Multi-provider storage",
      "Bring your own database",
    ],
    url: "",
    cta: "Visit blocks Data",
  },
  {
    name: "blocks-logic",
    appName: "blocks Logic",
    badge: "Worflow Engine",
    tagline: "Automate without writing code.",
    descriptionTitle: "Build workflows on a canvas",
    keywords: ["visual", "automated", "event-driven", "codeless"],
    shortDescription:
      "Orchestrate multi-step automations visually with drag-and-drop triggers, AI nodes, and external API integrations without coding.",
    description:
      tStatic("utilities.shared", "BLOCKS_PRODUCTS.BLOCKS_LOGIC_IS_A_VISUAL"),
    featureChips: [
      "Drag-and-drop builder",
      "Webhook & email triggers",
      "AI agent node",
      "HTTP & database actions",
      "Conditional branching",
    ],
    url: "",
    cta: "Visit blocks Logic",
  },
  {
    name: "blocks-studio",
    appName: "blocks Studio",
    badge: "AI Studio",
    tagline: "Generate AI-powered apps within Blocks context.",
    descriptionTitle: "Generate anything with AI",
    keywords: [
      "intelligent",
      "generative",
      "contextual",
      "instant",
      "adaptive",
    ],
    shortDescription:
      "Generate working applications from natural language prompts within the Blocks context.",
    description:
      tStatic("utilities.shared", "BLOCKS_PRODUCTS.BLOCKS_STUDIO_IS_AN_AI"),
    featureChips: [
      "Natural language prompts",
      "AI app generation",
      "Blocks-aware context",
      "Instant prototyping",
      "Internal tool scaffolding",
    ],
    url: "",
    cta: "Visit blocks Studio",
  },
  {
    name: "blocks-agents",
    appName: "Blocks Agents",
    badge: "AI Gateway",
    tagline: "Reasoning agents for every frontend.",
    descriptionTitle: "AI agents that understand your product",
    keywords: [
      "reasoning",
      "autonomous",
      "embeddable",
      "intelligent",
      "adaptive",
    ],
    shortDescription:
      "Create and deploy AI agents with RAG knowledge bases, custom LLM support, and embeddable script tags.",
    description:
      tStatic("utilities.shared", "BLOCKS_PRODUCTS.BLOCKS_AGENTS_LETS_YOU_CREATE"),
    featureChips: [
      "RAG knowledge bases",
      "Custom LLM & MCP",
      "Embeddable script tag",
      "Standard APIs",
    ],
    url: "",
    cta: "Visit Blocks Agents",
  },
  {
    name: "blocks-release",
    appName: "blocks Release",
    badge: "CI/CD",
    tagline: "Secure releases with faster pace.",
    descriptionTitle: "Deployment with built-in guardrails",
    keywords: ["secure", "compliant", "automated", "fast"],
    shortDescription:
      "Manage end-to-end deployment with git-based CI/CD, branch-environment mapping, and automated security scanning for ISO compliance.",
    description:
      tStatic("utilities.shared", "BLOCKS_PRODUCTS.BLOCKS_RELEASE_PROVIDES_END_TO"),
    featureChips: [
      "Git-based deployment",
      "Branch-environment mapping",
      "SCA, SAST & DAST",
      "CI/CD pipelines",
      "ISO compliance",
    ],
    url: "",
    cta: "Visit blocks Release",
  },
  {
    name: "blocks-monitor",
    appName: "blocks Monitor",
    badge: "Uptime",
    tagline: "Full visibility from day one.",
    descriptionTitle: "Observability with zero integration time",
    keywords: ["observable", "traceable", "real-time", "intelligent"],
    shortDescription:
      "Get built-in observability with real-time logs, distributed tracing, usage metrics, and AI-assisted analysis. Zero setup required.",
    description:
      tStatic("utilities.shared", "BLOCKS_PRODUCTS.BLOCKS_MONITOR_DELIVERS_BUILT_IN"),
    featureChips: [
      "Real-time logs with AI",
      "Distributed tracing",
      "Usage metrics",
      "Health monitoring",
      "Zero external tooling",
    ],
    url: "",
    cta: "Visit blocks Monitor",
  },
  {
    name: "blocks-utilities",
    appName: "blocks Utilities",
    badge: "Toolkit",
    tagline: "Secure magic links for modern applications.",
    descriptionTitle: "Magic links, ready to use",
    keywords: ["secure", "automated", "time-limited", "scalable"],
    shortDescription:
      "Create secure time-limited magic links to power redirect and action-based automated workflows.",
    description:
      tStatic("utilities.shared", "BLOCKS_PRODUCTS.BLOCKS_UTILITIES_PROVIDES_SECURE"),
    featureChips: [
      "Redirect magic links",
      "Action magic links",
      "Workflow automation",
    ],
    url: "",
    cta: "Visit blocks Utilities",
  },
];
