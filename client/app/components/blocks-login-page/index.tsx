import { useEffect, useMemo, useRef, useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { Badge } from "@/components/ui-kits/badge/badge";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";
import { BLOCKS_PRODUCTS } from "@/constants/blocks-products";

export interface BlocksLoginPageProps {
  name: string;
  onLogin: () => void | Promise<void>;
  isLoading?: boolean;
  eyebrow?: string;
  keywords?: string[];
  keywordPrefix?: string;
  loginLabel?: string;
  docsUrl?: string;
  footerLink?: { label: string; url: string };
}

const DEFAULT_KEYWORDS = [
  "observable",
  "intelligent",
  "scalable",
  "resilient",
  "secure",
];

/** Split "blocks IAM" -> ["blocks", "IAM"] so the hero can render two lines. */
function splitAppName(appName: string): [string, string] {
  const idx = appName.indexOf(" ");
  if (idx === -1) return [appName, ""];
  return [appName.slice(0, idx), appName.slice(idx + 1)];
}

export function BlocksLoginPage({
  name,
  onLogin,
  isLoading = false,
  eyebrow = "Enterprise Application OS",
  keywords = DEFAULT_KEYWORDS,
  keywordPrefix = "Backends that are",
  loginLabel = "Log in to your account",
  docsUrl = "https://docs.seliseblocks.com/",
  footerLink = { label: "Visit Blocks", url: "https://seliseblocks.com" },
}: BlocksLoginPageProps) {
  const active = useMemo(
    () => BLOCKS_PRODUCTS.find((p) => p.name === name) ?? BLOCKS_PRODUCTS[0],
    [name],
  );
  const otherProducts = useMemo(
    () => BLOCKS_PRODUCTS.filter((p) => p.name !== active.name),
    [active.name],
  );
  const [titleHead, titleTail] = splitAppName(active.appName);
  const navLabel = active.badge;
  const heroSubtitle = active.tagline;
  const features = active.featureChips;
  const derivedKeywordPrefix = active.descriptionTitle;
  const derivedKeywords = active.keywords;

  const [keywordIdx, setKeywordIdx] = useState(0);
  const [keywordVisible, setKeywordVisible] = useState(true);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);

  const resolvedKeywords = useMemo(() => derivedKeywords, [derivedKeywords]);

  useEffect(() => {
    const id = setInterval(() => {
      setKeywordVisible(false);
      setTimeout(() => {
        setKeywordIdx((p) => (p + 1) % resolvedKeywords.length);
        setKeywordVisible(true);
      }, 280);
    }, 2800);
    return () => clearInterval(id);
  }, [resolvedKeywords.length]);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    let raf = 0;
    let t = 0;
    let dpr = window.devicePixelRatio || 1;
    let w = 0;
    let h = 0;

    const resize = () => {
      dpr = window.devicePixelRatio || 1;
      w = canvas.width = Math.floor(window.innerWidth * dpr);
      h = canvas.height = Math.floor(window.innerHeight * dpr);
      canvas.style.width = window.innerWidth + "px";
      canvas.style.height = window.innerHeight + "px";
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    };

    const hslToRgb = (hue: number, s: number, l: number) => {
      s /= 100;
      l /= 100;
      const k = (n: number) => (n + hue / 30) % 12;
      const a = s * Math.min(l, 1 - l);
      const f = (n: number) =>
        l - a * Math.max(-1, Math.min(k(n) - 3, Math.min(9 - k(n), 1)));
      return [
        Math.round(f(0) * 255),
        Math.round(f(8) * 255),
        Math.round(f(4) * 255),
      ];
    };

    const draw = () => {
      const time = t * 0.008;
      const baseHue = 185 + 15 * Math.sin(time);
      const c1 = hslToRgb(baseHue, 100, 50);
      const c2 = hslToRgb(baseHue + 15, 100, 50);
      const c3 = hslToRgb(baseHue - 15, 100, 50);
      const cx = (w / dpr) * 0.5;
      const cy = (h / dpr) * 0.5;
      ctx.clearRect(0, 0, w / dpr, h / dpr);

      const r1 = (Math.max(w, h) / dpr) * 0.6;
      const g1 = ctx.createRadialGradient(
        cx * 0.6,
        cy * 0.7,
        0,
        cx * 0.6,
        cy * 0.7,
        r1,
      );
      g1.addColorStop(0, `rgba(${c1[0]},${c1[1]},${c1[2]},0.18)`);
      g1.addColorStop(1, `rgba(${c1[0]},${c1[1]},${c1[2]},0)`);
      ctx.fillStyle = g1;
      ctx.fillRect(0, 0, w / dpr, h / dpr);

      const r2 = (Math.max(w, h) / dpr) * 0.5;
      const g2 = ctx.createRadialGradient(
        cx * 1.3,
        cy * 0.4,
        0,
        cx * 1.3,
        cy * 0.4,
        r2,
      );
      g2.addColorStop(0, `rgba(${c2[0]},${c2[1]},${c2[2]},0.12)`);
      g2.addColorStop(1, `rgba(${c2[0]},${c2[1]},${c2[2]},0)`);
      ctx.fillStyle = g2;
      ctx.fillRect(0, 0, w / dpr, h / dpr);

      const r3 = (Math.max(w, h) / dpr) * 0.45;
      const g3 = ctx.createRadialGradient(
        cx * 0.3,
        cy * 1.2,
        0,
        cx * 0.3,
        cy * 1.2,
        r3,
      );
      g3.addColorStop(0, `rgba(${c3[0]},${c3[1]},${c3[2]},0.10)`);
      g3.addColorStop(1, `rgba(${c3[0]},${c3[1]},${c3[2]},0)`);
      ctx.fillStyle = g3;
      ctx.fillRect(0, 0, w / dpr, h / dpr);

      t++;
      raf = requestAnimationFrame(draw);
    };

    resize();
    window.addEventListener("resize", resize);
    raf = requestAnimationFrame(draw);
    return () => {
      cancelAnimationFrame(raf);
      window.removeEventListener("resize", resize);
    };
  }, []);

  // Duplicate for seamless infinite scroll
  const carouselCards = [...otherProducts, ...otherProducts];

  return (
    <div className="blocksLogin-page">
      <style>{blocksLoginStyles}</style>

      <div className="grid-bg" />
      <div className="scan-line" />
      <div className="radial-glow" />
      <div className="secondary-glow" />
      <div className="vignette" />
      <div className="noise-overlay" />
      <canvas className="atmospheric-canvas" ref={canvasRef} />

      <div className="corner corner-tl" />
      <div className="corner corner-tr" />
      <div className="corner corner-bl" />
      <div className="corner corner-br" />
      <div className="corner-dot corner-dot-tl" />
      <div className="corner-dot corner-dot-tr" />
      <div className="corner-dot corner-dot-bl" />
      <div className="corner-dot corner-dot-br" />

      <div
        className="particle"
        style={{
          left: "6%",
          animationDuration: "16s",
          animationDelay: "0s",
          width: 2,
          height: 2,
        }}
      />
      <div
        className="particle"
        style={{
          left: "18%",
          animationDuration: "20s",
          animationDelay: "3s",
          width: 1.5,
          height: 1.5,
        }}
      />
      <div
        className="particle large"
        style={{
          left: "35%",
          animationDuration: "14s",
          animationDelay: "1.5s",
          width: 3,
          height: 3,
        }}
      />
      <div
        className="particle"
        style={{
          left: "52%",
          animationDuration: "18s",
          animationDelay: "5s",
          width: 2,
          height: 2,
        }}
      />
      <div
        className="particle"
        style={{
          left: "68%",
          animationDuration: "22s",
          animationDelay: "2s",
          width: 1,
          height: 1,
        }}
      />
      <div
        className="particle large"
        style={{
          left: "82%",
          animationDuration: "15s",
          animationDelay: "4s",
          width: 2.5,
          height: 2.5,
        }}
      />
      <div
        className="particle"
        style={{
          left: "92%",
          animationDuration: "19s",
          animationDelay: "6s",
          width: 1.5,
          height: 1.5,
        }}
      />

      <nav className="site-nav">
        <div className="nav-left">
          <img
            src="/blocks-logos/iam_light_mode.svg"
            className="nav-logo-mark dark:hidden"
          />
          <img
            src="/blocks-logos/iam_dark_mode.svg"
            className="nav-logo-mark hidden dark:block"
          />
        </div>
        <div className="nav-right">
          <nav className="nav-links">
            <a
              href={docsUrl}
              target="_blank"
              rel="noreferrer"
              className="nav-link"
            >
              Docs
            </a>
            <a
              href="https://seliseblocks.com"
              target="_blank"
              rel="noreferrer"
              className="nav-link"
            >
              Blocks
            </a>
            <a
              href="https://github.com/SELISEdigitalplatforms"
              target="_blank"
              rel="noreferrer"
              className="nav-link"
            >
              GitHub
            </a>
          </nav>
          <ModeToggle />
        </div>
      </nav>

      <main className="main">
        <div className="col-left">
          <p className="eyebrow">{eyebrow}</p>
          <h1 className="title-main">
            {titleHead}
            {titleTail && (
              <>
                <br />
                {titleTail}
              </>
            )}
          </h1>
          <p className="title-sub">{heroSubtitle}</p>
          <p className="keywords">
            {derivedKeywordPrefix}{" "}
            <span
              className="keyword-anim"
              style={{ opacity: keywordVisible ? 1 : 0 }}
            >
              {resolvedKeywords[keywordIdx]}
            </span>
          </p>
          <p className="desc">{active.description}</p>

          {features.length > 0 && (
            <div className="features">
              {features.map((f) => (
                <Badge key={f} variant="outline" className="feature-pill">
                  {f}
                </Badge>
              ))}
            </div>
          )}

          <div className="cta-row">
            <div className="button-container">
              <div className="button-ring" />
              <div className="button-ring" />
              <Button
                className="launch-btn blocks-gradient"
                disabled={isLoading}
                onClick={onLogin}
              >
                {isLoading ? "Redirecting…" : loginLabel}
              </Button>
            </div>
            <a
              href={docsUrl}
              target="_blank"
              rel="noreferrer"
              className="cta-docs"
            >
              View documentation
              <svg
                width="12"
                height="12"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2.5"
              >
                <path d="M5 12h14M12 5l7 7-7 7" />
              </svg>
            </a>
          </div>
        </div>

        <div className="col-right">
          <div className="sdk-header-row">
            <p className="sdk-header-label">Core Services — Blocks Platform</p>
            <Badge variant="outline" className="sdk-count-badge">
              {otherProducts.length} services
            </Badge>
          </div>

          <div className="carousel-track">
            <div className="carousel-inner">
              {carouselCards.map((p, i) => (
                <div className="sdk-card" key={`${p.name}-${i}`}>
                  <div className="sdk-card-top">
                    <span className="sdk-name">{p.appName}</span>
                    <Badge variant="outline" className="sdk-badge">
                      {p.badge}
                    </Badge>
                  </div>
                  <div className="sdk-card-body">
                    <p className="sdk-desc">{p.shortDescription}</p>
                    <div className="sdk-links">
                      {p.featureChips.slice(0, 4).map((chip) => (
                        <Badge
                          key={chip}
                          variant="outline"
                          className="sdk-link dim"
                        >
                          {chip}
                        </Badge>
                      ))}
                    </div>
                  </div>
                  {p.url && (
                    <div className="sdk-card-footer">
                      <a
                        href={p.url}
                        target="_blank"
                        rel="noreferrer"
                        className="sdk-cta"
                      >
                        {p.cta}
                        <svg
                          width="10"
                          height="10"
                          viewBox="0 0 24 24"
                          fill="none"
                          stroke="currentColor"
                          strokeWidth="2.5"
                        >
                          <path d="M5 12h14M12 5l7 7-7 7" />
                        </svg>
                      </a>
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>

          <div className="sdk-footer">
            <a
              href={footerLink.url}
              target="_blank"
              rel="noreferrer"
              className="visit-construct"
            >
              {footerLink.label}
              <svg
                width="12"
                height="12"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2.5"
              >
                <path d="M5 12h14M12 5l7 7-7 7" />
              </svg>
            </a>
            <span className="sdk-open-source">Open source</span>
          </div>
        </div>
      </main>
    </div>
  );
}

const blocksLoginStyles = `
.blocksLogin-page {
  --bg:      #050510;
  --surface: #0a0a1a;
  --surface-elevated: #0f0f22;
  --fg:      #e8e8f0;
  --muted:   #5e5e7a;
  --border:  #16162a;
  --border-hover: rgba(0,102,178,0.18);
  --accent:  #0066b2;
  --accent-glow: rgba(0,102,178,0.35);
  --accent2: #00B2FF;
  --accent2-glow: rgba(0,178,255,0.30);
  --nav-h:   56px;
  --ease-out-expo: cubic-bezier(0.16, 1, 0.3, 1);
  --ease-in-out-sine: cubic-bezier(0.37, 0, 0.63, 1);

  position: fixed;
  inset: 0;
  overflow: hidden;
  background: var(--bg);
  color: var(--fg);
  display: flex;
  flex-direction: column;
  transition: background 0.4s var(--ease-out-expo), color 0.4s var(--ease-out-expo);
}
:root:not(.dark) .blocksLogin-page {
  --bg:      #f4f4f8;
  --surface: #ffffff;
  --surface-elevated: #f8f8fc;
  --fg:      #111120;
  --muted:   #6b6b80;
  --border:  #e0e0ec;
  --border-hover: rgba(0,102,178,0.22);
  --accent:  #0066b2;
  --accent-glow: rgba(0,102,178,0.25);
  --accent2: #0099dd;
  --accent2-glow: rgba(0,153,221,0.20);
}
.blocksLogin-page *, .blocksLogin-page *::before, .blocksLogin-page *::after { box-sizing: border-box; }

.blocksLogin-page .grid-bg {
  position: absolute; inset: 0;
  background:
    linear-gradient(90deg, transparent 49.8%, rgba(0,102,178,0.035) 50%, transparent 50.2%),
    linear-gradient(0deg,  transparent 49.8%, rgba(0,102,178,0.035) 50%, transparent 50.2%);
  background-size: 80px 80px;
  animation: blocksLogin-gridPulse 8s var(--ease-in-out-sine) infinite;
  pointer-events: none; z-index: 0;
}
:root:not(.dark) .blocksLogin-page .grid-bg { opacity: 0.18; background-size: 100px 100px; }
@keyframes blocksLogin-gridPulse { 0%,100%{opacity:0.25} 50%{opacity:0.55} }

.blocksLogin-page .scan-line {
  position: absolute; top: -2px; left: 0; right: 0; height: 1.5px;
  background: linear-gradient(90deg, transparent 5%, var(--accent) 50%, transparent 95%);
  animation: blocksLogin-scanMove 7s linear infinite;
  opacity: 0.25; z-index: 50; pointer-events: none; filter: blur(0.3px);
}
@keyframes blocksLogin-scanMove { 0%{top:-2px} 100%{top:100vh} }

.blocksLogin-page .radial-glow {
  position: absolute; top: 55%; left: 25%;
  transform: translate(-50%, -50%); width: 700px; height: 700px;
  background: radial-gradient(ellipse, var(--accent2-glow) 0%, transparent 60%);
  animation: blocksLogin-glowPulse 10s var(--ease-in-out-sine) infinite;
  pointer-events: none; z-index: 0;
}
:root:not(.dark) .blocksLogin-page .radial-glow { opacity: 0.12; }
@keyframes blocksLogin-glowPulse {
  0%,100%{opacity:0.3;transform:translate(-50%,-50%) scale(1)}
  50%{opacity:0.6;transform:translate(-50%,-50%) scale(1.08)}
}

.blocksLogin-page .secondary-glow {
  position: absolute; top: 20%; right: 10%; width: 400px; height: 400px;
  background: radial-gradient(circle, var(--accent-glow) 0%, transparent 55%);
  opacity: 0.15;
  animation: blocksLogin-secondaryGlow 12s var(--ease-in-out-sine) infinite;
  pointer-events: none; z-index: 0;
}
@keyframes blocksLogin-secondaryGlow {
  0%,100%{opacity:0.1;transform:scale(1)} 50%{opacity:0.25;transform:scale(1.15)}
}

.blocksLogin-page .vignette {
  position: absolute; inset: 0;
  background: radial-gradient(ellipse at center, transparent 40%, rgba(0,0,0,0.4) 100%);
  pointer-events: none; z-index: 1;
}
:root:not(.dark) .blocksLogin-page .vignette {
  background: radial-gradient(ellipse at center, transparent 50%, rgba(0,0,0,0.06) 100%);
}

.blocksLogin-page .noise-overlay {
  position: absolute; inset: 0; opacity: 0.025; pointer-events: none; z-index: 2;
  background-image: url("data:image/svg+xml,%3Csvg viewBox='0 0 256 256' xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='noise'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='4' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23noise)'/%3E%3C/svg%3E");
  background-repeat: repeat; background-size: 256px 256px;
}
:root:not(.dark) .blocksLogin-page .noise-overlay { opacity: 0.015; }

.blocksLogin-page .atmospheric-canvas {
  position: absolute; inset: 0; width: 100%; height: 100%;
  pointer-events: none; z-index: 0; opacity: 0.55; mix-blend-mode: screen;
}
:root:not(.dark) .blocksLogin-page .atmospheric-canvas { opacity: 0.22; mix-blend-mode: multiply; }

.blocksLogin-page .corner {
  position: absolute; width: 48px; height: 48px;
  border: 1.5px solid var(--accent); opacity: 0.18;
  z-index: 100; pointer-events: none;
  transition: opacity 0.4s var(--ease-out-expo);
}
:root:not(.dark) .blocksLogin-page .corner { opacity: 0.12; }
.blocksLogin-page .corner-tl { top: 20px; left: 20px; border-right: none; border-bottom: none; }
.blocksLogin-page .corner-tr { top: 20px; right: 20px; border-left: none; border-bottom: none; }
.blocksLogin-page .corner-bl { bottom: 20px; left: 20px; border-right: none; border-top: none; }
.blocksLogin-page .corner-br { bottom: 20px; right: 20px; border-left: none; border-top: none; }

.blocksLogin-page .corner-dot {
  position: absolute; width: 3px; height: 3px; background: var(--accent);
  border-radius: 50%; opacity: 0.35; z-index: 100; pointer-events: none;
  animation: blocksLogin-dotPulse 4s ease-in-out infinite;
}
.blocksLogin-page .corner-dot-tl { top: 18px; left: 18px; }
.blocksLogin-page .corner-dot-tr { top: 18px; right: 18px; }
.blocksLogin-page .corner-dot-bl { bottom: 18px; left: 18px; }
.blocksLogin-page .corner-dot-br { bottom: 18px; right: 18px; }
@keyframes blocksLogin-dotPulse {
  0%,100% { opacity: 0.2; box-shadow: 0 0 4px var(--accent); }
  50%     { opacity: 0.5; box-shadow: 0 0 10px var(--accent); }
}

.blocksLogin-page .particle {
  position: absolute; background: var(--accent); border-radius: 50%; opacity: 0;
  animation: blocksLogin-particleFloat linear infinite;
  pointer-events: none; z-index: 0; filter: blur(0.5px);
}
.blocksLogin-page .particle.large { filter: blur(1px); }
@keyframes blocksLogin-particleFloat {
  0%   { opacity: 0; transform: translateY(100vh) scale(0.5); }
  5%   { opacity: 0.4; }
  95%  { opacity: 0.4; }
  100% { opacity: 0; transform: translateY(-20px) scale(1); }
}

.blocksLogin-page .site-nav {
  position: absolute; top: 0; left: 0; right: 0; height: var(--nav-h);
  display: flex; align-items: center; justify-content: space-between;
  padding: 0 44px; z-index: 200;
  background: rgba(5,5,16,0.65);
  backdrop-filter: blur(20px) saturate(1.2);
  -webkit-backdrop-filter: blur(20px) saturate(1.2);
  border-bottom: 1px solid var(--border);
  transition: background 0.4s var(--ease-out-expo), border-color 0.4s var(--ease-out-expo);
  opacity: 0; transform: translateY(-10px);
  animation: blocksLogin-navEnter 0.8s var(--ease-out-expo) 0.2s forwards;
}
@keyframes blocksLogin-navEnter { to { opacity: 1; transform: translateY(0); } }
:root:not(.dark) .blocksLogin-page .site-nav { background: rgba(244,244,248,0.72); }

.blocksLogin-page .nav-left { display: flex; align-items: center; gap: 14px; }
.blocksLogin-page .nav-logo-mark {
  height: 26px; width: auto;
  transition: opacity 0.3s; flex-shrink: 0;
}
.blocksLogin-page .nav-divider { width: 1px; height: 16px; background: var(--border); }
.blocksLogin-page .nav-product {
  font-size: 0.7rem; font-weight: 600; letter-spacing: 0.22em;
  text-transform: uppercase; color: var(--fg);
}
.blocksLogin-page .nav-right { display: flex; align-items: center; gap: 28px; }
.blocksLogin-page .nav-links { display: flex; align-items: center; gap: 26px; }
.blocksLogin-page .nav-link {
  font-size: 0.68rem; letter-spacing: 0.12em; text-transform: uppercase;
  color: var(--muted); text-decoration: none; font-weight: 500;
  transition: color 0.25s ease, text-shadow 0.25s ease; position: relative;
}
.blocksLogin-page .nav-link::after {
  content: ''; position: absolute; bottom: -4px; left: 0;
  width: 0; height: 1px; background: var(--accent);
  transition: width 0.3s var(--ease-out-expo);
}
.blocksLogin-page .nav-link:hover { color: var(--fg); text-shadow: 0 0 12px var(--accent-glow); }
.blocksLogin-page .nav-link:hover::after { width: 100%; }

.blocksLogin-page .main {
  flex: 1; min-height: 0; display: grid;
  grid-template-columns: 1fr 400px; gap: 56px;
  padding: calc(var(--nav-h) + 48px) 52px 48px;
  max-width: 1420px; margin: 0 auto; width: 100%;
  position: relative; z-index: 10; overflow: hidden;
}

.blocksLogin-page .col-left { display: flex; flex-direction: column; justify-content: center; min-height: 0; }

.blocksLogin-page .eyebrow {
  font-size: 0.6rem; letter-spacing: 0.35em; text-transform: uppercase;
  color: var(--accent); margin-bottom: 20px; font-weight: 600;
  display: flex; align-items: center; gap: 12px;
  opacity: 0; transform: translateY(12px);
  animation: blocksLogin-fadeUp 0.7s var(--ease-out-expo) 0.4s forwards;
}
.blocksLogin-page .eyebrow::before {
  content: ''; display: block; width: 28px; height: 1px;
  background: var(--accent); opacity: 0.6; flex-shrink: 0;
}

.blocksLogin-page .title-main {
  font-size: clamp(2rem, 3.8vw, 3.1rem); font-weight: 700;
  font-family: ui-monospace, "SF Mono", SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", monospace;
  letter-spacing: 0.06em; text-transform: uppercase; line-height: 1.08;
  background: linear-gradient(135deg, var(--fg) 30%, var(--accent) 100%);
  -webkit-background-clip: text; -webkit-text-fill-color: transparent; background-clip: text;
  margin-bottom: 12px; opacity: 0; transform: translateY(16px);
  animation: blocksLogin-fadeUp 0.8s var(--ease-out-expo) 0.55s forwards;
  filter: drop-shadow(0 0 30px rgba(0,102,178,0.08));
}

.blocksLogin-page .title-sub {
  font-size: clamp(0.68rem, 1vw, 0.82rem); font-weight: 400;
  letter-spacing: 0.28em; text-transform: uppercase;
  color: var(--muted); margin-bottom: 32px;
  opacity: 0; transform: translateY(12px);
  animation: blocksLogin-fadeUp 0.7s var(--ease-out-expo) 0.7s forwards;
}

.blocksLogin-page .keywords {
  font-size: 0.9rem; letter-spacing: 0.06em; text-transform: uppercase;
  color: var(--fg); margin-bottom: 24px; font-weight: 500;
  opacity: 0; transform: translateY(12px);
  animation: blocksLogin-fadeUp 0.7s var(--ease-out-expo) 0.85s forwards;
}
.blocksLogin-page .keyword-anim {
  color: var(--accent); font-weight: 700; display: inline-block; min-width: 8ch;
  transition: opacity 0.3s ease; text-shadow: 0 0 16px var(--accent-glow); position: relative;
}
.blocksLogin-page .keyword-anim::after {
  content: ''; position: absolute; right: -3px; top: 0; bottom: 0;
  width: 2px; background: var(--accent);
  animation: blocksLogin-cursorBlink 1s step-end infinite;
}
@keyframes blocksLogin-cursorBlink { 0%,100%{opacity:1} 50%{opacity:0} }

.blocksLogin-page .desc {
  font-size: 1rem; line-height: 1.75; color: var(--fg); max-width: 520px;
  margin-bottom: 32px; font-weight: 400;
  opacity: 0; transform: translateY(12px);
  animation: blocksLogin-fadeUp 0.7s var(--ease-out-expo) 1s forwards;
}
.blocksLogin-page .desc .highlight { color: var(--accent); font-weight: 600; }
@keyframes blocksLogin-fadeUp { to { opacity: 0.85; transform: translateY(0); } }

.blocksLogin-page .features {
  display: flex; gap: 10px; flex-wrap: wrap; margin-bottom: 44px;
  opacity: 0; transform: translateY(12px);
  animation: blocksLogin-fadeUp 0.7s var(--ease-out-expo) 1.15s forwards;
}
.blocksLogin-page .feature-pill {
  font-size: 0.62rem; letter-spacing: 0.12em; text-transform: uppercase;
  color: var(--muted); padding: 6px 14px; border: 1px solid var(--border);
  border-radius: 6px; font-weight: 500; background: rgba(255,255,255,0.02);
  transition: border-color 0.25s ease, color 0.25s ease, background 0.25s ease, box-shadow 0.25s ease;
  cursor: default;
}
:root:not(.dark) .blocksLogin-page .feature-pill { background: rgba(0,0,0,0.015); }
.blocksLogin-page .feature-pill:hover {
  border-color: var(--border-hover); color: var(--fg);
  box-shadow: 0 0 12px rgba(0,102,178,0.06);
}

.blocksLogin-page .cta-row {
  display: flex; align-items: center; gap: 28px; flex-wrap: wrap;
  opacity: 0; transform: translateY(12px);
  animation: blocksLogin-fadeUp 0.7s var(--ease-out-expo) 1.3s forwards;
}
.blocksLogin-page .button-container { position: relative; display: inline-block; }
.blocksLogin-page .button-ring {
  position: absolute; inset: -14px;
  border: 1px solid rgba(0,102,178,0.15); border-radius: 100px;
  animation: blocksLogin-ringPulse 3s var(--ease-in-out-sine) infinite; pointer-events: none;
}
.blocksLogin-page .button-ring:nth-child(2) {
  inset: -26px; border-color: rgba(0,178,255,0.1); animation-delay: 1.2s;
}
@keyframes blocksLogin-ringPulse {
  0%,100% { opacity: 0.15; transform: scale(1); } 50% { opacity: 0.4; transform: scale(1.03); }
}
.blocksLogin-page .launch-btn {
  position: relative; padding: 16px 44px;
  font-size: 0.78rem; font-weight: 700; letter-spacing: 0.22em; text-transform: uppercase;
  color: #fff;
  border: none; border-radius: 6px; cursor: pointer; overflow: hidden;
  transition: transform 0.25s var(--ease-out-expo), box-shadow 0.25s ease;
  box-shadow: 0 4px 24px rgba(0,102,178,0.15), 0 0 0 1px rgba(0,102,178,0.1) inset;
}
.blocksLogin-page .launch-btn::before {
  content: ''; position: absolute; top: 0; left: -100%; width: 100%; height: 100%;
  background: linear-gradient(90deg, transparent, rgba(255,255,255,0.25), transparent);
  transition: left 0.6s var(--ease-out-expo);
}
.blocksLogin-page .launch-btn:hover:not(:disabled) {
  transform: scale(1.04) translateY(-1px);
  box-shadow: 0 8px 32px rgba(0,102,178,0.25), 0 0 0 1px rgba(0,102,178,0.15) inset, 0 0 60px rgba(0,178,255,0.1);
}
.blocksLogin-page .launch-btn:hover:not(:disabled)::before { left: 100%; }
.blocksLogin-page .launch-btn:active:not(:disabled) { transform: scale(0.98); }
.blocksLogin-page .launch-btn:disabled { opacity: 0.7; cursor: not-allowed; }

.blocksLogin-page .cta-docs {
  font-size: 0.7rem; letter-spacing: 0.14em; text-transform: uppercase;
  color: var(--muted); text-decoration: none; display: inline-flex;
  align-items: center; gap: 8px; font-weight: 500;
  transition: color 0.25s ease, gap 0.25s var(--ease-out-expo); position: relative;
}
.blocksLogin-page .cta-docs::after {
  content: ''; position: absolute; bottom: -2px; left: 0;
  width: 0; height: 1px; background: var(--accent);
  transition: width 0.3s var(--ease-out-expo);
}
.blocksLogin-page .cta-docs:hover { color: var(--fg); gap: 12px; }
.blocksLogin-page .cta-docs:hover::after { width: 100%; }
.blocksLogin-page .cta-docs svg { transition: transform 0.25s var(--ease-out-expo); }
.blocksLogin-page .cta-docs:hover svg { transform: translateX(4px); }

.blocksLogin-page .col-right {
  display: flex; flex-direction: column; min-height: 0; overflow: hidden;
  opacity: 0; transform: translateX(20px);
  animation: blocksLogin-fadeRight 0.9s var(--ease-out-expo) 0.9s forwards;
}
@keyframes blocksLogin-fadeRight { to { opacity: 1; transform: translateX(0); } }

.blocksLogin-page .sdk-header-row {
  display: flex; align-items: center; justify-content: space-between;
  margin-bottom: 16px; flex-shrink: 0;
}
.blocksLogin-page .sdk-header-label {
  font-size: 0.58rem; letter-spacing: 0.32em; text-transform: uppercase; color: var(--muted);
}
.blocksLogin-page .sdk-count-badge {
  font-size: 0.55rem; letter-spacing: 0.14em; text-transform: uppercase;
  color: var(--muted); border: 1px solid var(--border); padding: 3px 10px;
  border-radius: 4px; font-weight: 500;
}

.blocksLogin-page .carousel-track {
  position: relative; flex: 1; min-height: 0; max-height: 430px; overflow: hidden;
  mask-image: linear-gradient(to bottom, transparent, black 8%, black 92%, transparent);
  -webkit-mask-image: linear-gradient(to bottom, transparent, black 8%, black 92%, transparent);
}
.blocksLogin-page .carousel-inner {
  display: flex; flex-direction: column; gap: 12px;
  animation: blocksLogin-scrollV 40s linear infinite; will-change: transform;
}
@keyframes blocksLogin-scrollV { 0%{transform:translateY(0)} 100%{transform:translateY(-50%)} }
.blocksLogin-page .carousel-inner:hover { animation-play-state: paused; }

.blocksLogin-page .sdk-card {
  background: var(--surface); border: 1px solid var(--border);
  border-radius: 8px; padding: 16px 18px; flex-shrink: 0;
  transition: border-color 0.3s ease, box-shadow 0.3s ease, transform 0.3s var(--ease-out-expo), background 0.3s;
  position: relative; overflow: hidden;
}
.blocksLogin-page .sdk-card::before {
  content: ''; position: absolute; top: 0; left: 0; right: 0; height: 1px;
  background: linear-gradient(90deg, transparent, var(--accent), transparent);
  opacity: 0; transition: opacity 0.3s ease;
}
.blocksLogin-page .sdk-card:hover {
  border-color: var(--border-hover);
  box-shadow: 0 0 20px rgba(0,102,178,0.06), 0 4px 16px rgba(0,0,0,0.15);
  transform: translateX(-4px); background: var(--surface-elevated);
}
.blocksLogin-page .sdk-card:hover::before { opacity: 0.4; }

.blocksLogin-page .sdk-card-top {
  display: flex; align-items: center; justify-content: space-between; margin-bottom: 10px;
}
.blocksLogin-page .sdk-name {
  font-size: 0.78rem; font-weight: 600; letter-spacing: 0.14em; text-transform: uppercase;
  color: var(--fg); transition: text-shadow 0.25s;
}
.blocksLogin-page .sdk-card:hover .sdk-name { text-shadow: 0 0 12px var(--accent-glow); }
.blocksLogin-page .sdk-badge {
  font-size: 0.5rem; letter-spacing: 0.16em; text-transform: uppercase;
  padding: 3px 9px; border-radius: 4px;
  background: rgba(0,102,178,0.06); color: var(--accent);
  border: 1px solid rgba(0,102,178,0.15); font-weight: 700; white-space: nowrap;
}

.blocksLogin-page .sdk-desc {
  font-size: 0.78rem; color: var(--muted); line-height: 1.6;
  margin-bottom: 12px; min-height: 52px; font-weight: 400; transition: color 0.25s;
}
.blocksLogin-page .sdk-card:hover .sdk-desc { color: var(--fg); }

.blocksLogin-page .sdk-links { display: flex; gap: 8px; flex-wrap: wrap; }
.blocksLogin-page .sdk-link {
  font-size: 0.56rem; letter-spacing: 0.12em; text-transform: uppercase;
  color: var(--accent); text-decoration: none; padding: 4px 10px;
  border: 1px solid rgba(0,102,178,0.18); border-radius: 4px; font-weight: 600;
  transition: background 0.2s, border-color 0.2s, box-shadow 0.2s;
}
.blocksLogin-page .sdk-link:hover {
  background: rgba(0,102,178,0.08); border-color: var(--accent);
  box-shadow: 0 0 8px rgba(0,102,178,0.08);
}
.blocksLogin-page .sdk-link.dim { color: var(--muted); border-color: var(--border); }

.blocksLogin-page .sdk-card-footer {
  margin-top: 12px; padding-top: 10px; border-top: 1px dashed var(--border);
}
.blocksLogin-page .sdk-cta {
  display: inline-flex; align-items: center; gap: 6px;
  font-size: 0.58rem; letter-spacing: 0.16em; text-transform: uppercase;
  color: var(--accent2); text-decoration: none; font-weight: 700;
  transition: gap 0.25s var(--ease-out-expo), text-shadow 0.25s;
}
.blocksLogin-page .sdk-cta:hover { gap: 10px; text-shadow: 0 0 10px var(--accent2-glow); }

.blocksLogin-page .sdk-footer {
  display: flex; align-items: center; justify-content: space-between;
  margin-top: 16px; padding-top: 14px; border-top: 1px solid var(--border);
  flex-shrink: 0; opacity: 0; transform: translateY(8px);
  animation: blocksLogin-fadeUp 0.7s var(--ease-out-expo) 1.5s forwards;
}
.blocksLogin-page .visit-construct {
  display: inline-flex; align-items: center; gap: 8px;
  font-size: 0.65rem; letter-spacing: 0.16em; text-transform: uppercase;
  color: var(--accent2); text-decoration: none; font-weight: 600;
  transition: gap 0.25s var(--ease-out-expo), opacity 0.25s, text-shadow 0.25s; position: relative;
}
.blocksLogin-page .visit-construct::after {
  content: ''; position: absolute; bottom: -2px; left: 0;
  width: 0; height: 1px; background: var(--accent2);
  transition: width 0.3s var(--ease-out-expo);
}
.blocksLogin-page .visit-construct:hover {
  gap: 12px; opacity: 0.8; text-shadow: 0 0 12px var(--accent2-glow);
}
.blocksLogin-page .visit-construct:hover::after { width: 100%; }
.blocksLogin-page .sdk-open-source {
  font-size: 0.58rem; letter-spacing: 0.12em; text-transform: uppercase;
  color: var(--muted); opacity: 0.45; font-weight: 500;
}



@media (max-width: 960px) {
  .blocksLogin-page { position: relative; overflow: auto; min-height: 100vh; }
  .blocksLogin-page .main {
    grid-template-columns: 1fr;
    padding: calc(var(--nav-h) + 36px) 28px 80px;
    gap: 48px; overflow: visible;
  }
  .blocksLogin-page .col-left { justify-content: flex-start; }
  .blocksLogin-page .carousel-track {
    max-height: 280px;
    mask-image: linear-gradient(to right, transparent, black 5%, black 95%, transparent);
    -webkit-mask-image: linear-gradient(to right, transparent, black 5%, black 95%, transparent);
  }
  .blocksLogin-page .carousel-inner {
    flex-direction: row; animation: blocksLogin-scrollH 32s linear infinite; gap: 14px;
  }
  @keyframes blocksLogin-scrollH { 0%{transform:translateX(0)} 100%{transform:translateX(-50%)} }
  .blocksLogin-page .sdk-card { min-width: 280px; max-width: 280px; }
  .blocksLogin-page .sdk-desc { min-height: auto; }
  .blocksLogin-page .col-right {
    opacity: 1; transform: none;
    animation: blocksLogin-fadeUp 0.7s var(--ease-out-expo) 1.1s forwards;
  }
}

@media (max-width: 600px) {
  .blocksLogin-page .site-nav { padding: 0 22px; }
  .blocksLogin-page .nav-links { display: none; }
  .blocksLogin-page .main { padding: calc(var(--nav-h) + 28px) 22px 80px; }
  .blocksLogin-page .title-main { font-size: 1.8rem; }
  .blocksLogin-page .corner { width: 36px; height: 36px; }

  .blocksLogin-page .features { gap: 8px; }
  .blocksLogin-page .feature-pill { padding: 5px 10px; font-size: 0.58rem; }
  .blocksLogin-page .cta-row { gap: 18px; }
}

@media (prefers-reduced-motion: reduce) {
  .blocksLogin-page *, .blocksLogin-page *::before, .blocksLogin-page *::after {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
  }
}
`;
