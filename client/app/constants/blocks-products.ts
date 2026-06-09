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

export const BLOCKS_PRODUCTS: BlocksProduct[] = [
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
      "Blocks OS is a secure, isolated, and fully auditable tenant-level core operating layer. It handles multi-tenant isolation, secrets management, audit trails, and sensitive log and trace handling while automatically logging every authentication decision, data access event, and configuration change.",
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
      "Blocks Localization is a key-based and context-aware translation service. Manage multilingual interfaces, edit translations in-browser in real time through the extension, and run bulk operations across thousands of keys.",
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
      "Blocks IAM is the unified identity layer for your entire application ecosystem. Configure enterprise SSO, connect external identity providers like Keycloak, Okta, or Azure AD, and enforce MFA from a single secure dashboard.",
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
      "Blocks Data gives you instant GraphQL endpoints over managed or bring-your-own databases. Define schemas visually, enforce column-level validation and access control, and perform CRUD operations through the Data Playground.",
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
      "Blocks Logic is a visual workflow engine for orchestrating complex operations without code. Drag triggers, conditions, and actions onto a canvas, fire workflows through webhooks or email, execute AI reasoning nodes, and push data through HTTP requests.",
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
      "Blocks Studio is an AI engine that transforms natural language prompts into working applications and reports within the Blocks ecosystem. It uses the same agent infrastructure to convert prompts into functional interfaces.",
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
      "Blocks Agents lets you create, configure, and deploy intelligent assistants without complex technical setup. Equip agents with RAG-powered knowledge bases, connect custom LLMs through MCP, define tools and guardrails, and embed agents into any frontend with a single script tag.",
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
      "Blocks Release provides end-to-end deployment infrastructure with security and compliance built in. Connect your repositories and the platform handles builds, runtime, scaling, and observability automatically.",
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
      "Blocks Monitor delivers built-in observability without requiring external tooling or complex setup. Access real-time logs, distributed tracing, uptime monitoring, health checks, usage metrics, and AI-assisted diagnostics.",
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
    tagline: "Communication services for modern applications.",
    descriptionTitle: "Communication services, ready to use",
    keywords: ["reliable", "real-time", "automated", "scalable"],
    shortDescription:
      "Send branded emails, real-time notifications via SignalR, and secure magic links to power automated workflows.",
    description:
      "Blocks Utilities provides powerful communication services for your applications. Send branded emails, deliver real-time in-app notifications via SignalR, and create secure time-limited magic links—both redirect and action-based—to power automated workflows.",
    featureChips: [
      "Branded email",
      "Real-time notifications",
      "Redirect magic links",
      "Action magic links",
      "Workflow automation",
    ],
    url: "",
    cta: "Visit blocks Utilities",
  },
];
