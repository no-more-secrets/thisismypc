"use client";
import { useEffect, useState, type ReactNode } from "react";
import { Surface } from "./surface";
import { PatternBackground } from "./pattern-background";
const repo = "https://github.com/samboland/thisismypc";
const groups = [
    { name: "Windows", title: "Make Windows behave your way.", items: [["Windows Annoyances", "Control suggestions, ads, and unwanted features."], ["Privacy & Telemetry", "Choose supported privacy settings."], ["Windows Update", "Control update behavior."], ["Explorer & Context Menus", "Change preferences and disable registered right-click handlers."]] },
    { name: "Apps & system", title: "Bring the scattered controls together.", items: [["Software", "Install, remove, and update apps through winget."], ["Startup & Services", "Inspect startup entries and services."], ["Environment & Power Plans", "Edit variables, including PATH, and adjust power plans."], ["Network & Firewall", "Inspect rules and stage supported connection, DNS, and firewall changes."]] },
    { name: "Hardware", title: "Your hardware, in the same place.", items: [["Display", "Adjust supported monitors through DDC/CI."], ["Lighting", "Control supported devices through the bundled OpenRGB engine."], ["System Control", "Open maintained G-Helper controls on supported laptops."], ["Monitoring", "View supported memory, NVIDIA GPU, and basic battery readings."]] }
];
function Arrow() { return <svg className="button-arrow" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 12h15m-6-6 6 6-6 6"/></svg>; }
function Button({ children, href, primary = false }: {
    children: ReactNode;
    href: string;
    primary?: boolean;
}) { return <Surface as="a" href={href} className={"design-button" + (primary ? " is-primary" : "")}><span className="design-tint"/><span className="button-label">{children}</span><Surface as="span" className="design-icon-well"><Arrow /></Surface></Surface>; }
function Panel({ children, className = "" }: {
    children: ReactNode;
    className?: string;
}) { return <Surface as="div" className={"design-glass " + className}><span className="design-tint"/>{children}</Surface>; }
export function Site() {
    const [group, setGroup] = useState(0);
    const [backgroundEnabled, setBackgroundEnabled] = useState<boolean | null>(null);
    useEffect(() => {
        try { setBackgroundEnabled(localStorage.getItem("thisismypc.background.enabled") !== "0"); }
        catch { setBackgroundEnabled(true); }
    }, []);
    function changeBackground(enabled: boolean) {
        setBackgroundEnabled(enabled);
        try { localStorage.setItem("thisismypc.background.enabled", enabled ? "1" : "0"); } catch {}
    }
    const [copied, setCopied] = useState(false);
    const selected = groups[group];
    const command = ["git clone https://github.com/samboland/thisismypc.git", "cd thisismypc", ".\\Setup.ps1"].join("\n");
    return <div className="design-root tipc-site"><PatternBackground enabled={backgroundEnabled === true} /><a href="#main" className="skip">Skip to content</a>
    <header className="site-header"><div className="container header-inner"><a href="#" className="brand"><img src="/icon.png" alt="" width="44" height="44"/><span>ThisIsMyPC</span></a><nav aria-label="Main navigation"><a href="#the-app">The app</a><a href="#controls">What you control</a><a href={repo}>GitHub ↗</a></nav><Button href="#get-started">Get started</Button></div></header>
    <main id="main"><section className="container hero" aria-labelledby="hero-title"><div className="hero-intro"><div className="eyebrow"><span className="status-dot"/>FREE. OPEN SOURCE. YOURS.</div><h1 id="hero-title">This is <em>MY</em> PC.</h1><p className="hero-pitch">Debloat Windows entirely. Stop anything unwanted, control updates and telemetry, and manage all your hardware.</p><div className="hero-actions"><Button href="#get-started" primary>Take control</Button><a href={repo} className="text-link">Explore the source <span aria-hidden="true">↗</span></a></div><p className="requirements">For Windows 11 · Native desktop app · No accounts. No telemetry.</p></div><Panel className="ownership-note"><span className="note-number">01 / OWNERSHIP</span><p>You paid for it. Maybe you even built it yourself.</p><span>Controlling it should not take a dozen apps and a digital sparring match with a multibillion dollar corporation.</span></Panel></section>
    <section id="the-app" className="container app-section" aria-labelledby="app-heading"><div className="section-line"><h2 id="app-heading">Set it up your way.</h2><span>Inspect. Stage. Apply.</span></div><Panel className="app-frame"><img src="/thisismypc-presets.png" alt="ThisIsMyPC Presets page showing the Windows 10-ify pack and individual settings." width="1200" height="800" fetchPriority="high"/><div className="screenshot-caption"><span>THE ACTUAL APP</span><p>Presets bring related choices together. Every setting stays yours to change.</p></div></Panel></section>
    <section id="controls" className="container controls-section" aria-labelledby="controls-heading"><div className="section-intro"><div><p className="eyebrow">FEWER APPS. MORE CONTROL.</p><h2 id="controls-heading">All the bits of your PC.</h2></div><p>Windows settings and trusted hardware tools, brought together in one native app.</p></div><div className="control-tabs" role="tablist" aria-label="Control areas">{groups.map((item, index) => <Surface as="button" key={item.name} className={"control-tab" + (index === group ? " selected" : "")} role="tab" type="button" id={"tab-" + index} aria-selected={index === group} aria-controls="control-panel" tabIndex={index === group ? 0 : -1} onClick={() => setGroup(index)} onKeyDown={(event: React.KeyboardEvent<HTMLButtonElement>) => { let next = index; if (event.key === "ArrowRight")
        next = (index + 1) % groups.length;
    else if (event.key === "ArrowLeft")
        next = (index + groups.length - 1) % groups.length;
    else if (event.key === "Home")
        next = 0;
    else if (event.key === "End")
        next = groups.length - 1;
    else
        return; event.preventDefault(); setGroup(next); document.getElementById("tab-" + next)?.focus(); }}>{item.name}<span className="tab-number" aria-hidden="true">0{index + 1}</span></Surface>)}</div><Panel className="controls-panel"><div role="tabpanel" tabIndex={0} id="control-panel" aria-labelledby={"tab-" + group}><h3>{selected.title}</h3><div className="control-list">{selected.items.map(([name, description]) => <div key={name}><span className="tiny-cross" aria-hidden="true">+</span><div><h4>{name}</h4><p>{description}</p></div></div>)}</div></div></Panel><p className="footnote">Available controls depend on your Windows edition, policy state, and hardware.</p></section>
    <section className="container workflow-section" aria-labelledby="workflow-heading"><div className="workflow-lead"><p className="eyebrow">YOUR CHANGES. YOUR CALL.</p><h2 id="workflow-heading">Look first.<br />Change second.</h2><p>Changes stay staged until you review and approve them. Reversible changes capture their previous state for undo.</p><a className="text-link" href={repo + "#how-changes-work"}>How changes work ↗</a></div><div className="workflow-steps">{[["01", "Inspect", "See the settings in each preset. Choose what you want to change."], ["02", "Stage", "Review the changes together before applying them."], ["03", "Apply", "Approve your choices. Use undo for supported reversible changes."]].map(([number, title, text]) => <Panel key={number} className="workflow-step"><span>{number}</span><div><h3>{title}</h3><p>{text}</p></div></Panel>)}</div></section>
    <section id="get-started" className="container download-section" aria-labelledby="download-heading"><Panel className="download-panel"><div className="download-copy"><p className="eyebrow">DEVELOPMENT PREVIEW</p><h2 id="download-heading">Make yourself at home.</h2><p>The first release is still in development. There is no published installer yet.</p><p>Build the app, explore the code, and help test it now.</p><div className="download-actions"><Button href={repo + "#quick-start"} primary>Build from source</Button><a href={repo + "/releases"} className="text-link">Releases ↗</a></div><p className="requirements">Windows 11 22H2 or later · x64 · .NET 10</p></div><div className="command-panel"><div><span>POWERSHELL</span><button type="button" onClick={async () => { try {
        await navigator.clipboard.writeText(command);
        setCopied(true);
    }
    catch {
        setCopied(false);
    } }}>{copied ? "Copied" : "Copy"}</button></div><pre><code>{command}</code></pre><p>Setup checks prerequisites and builds the app. See the quick start before launching it.</p><a className="text-link" href={repo + "/blob/main/CONTRIBUTING.md"}>Development setup ↗</a><span className="sr-only" role="status">{copied ? "Setup commands copied." : ""}</span></div></Panel></section>
    </main><footer className="site-footer"><div className="container"><a href="#" className="brand"><img src="/icon.png" alt="" width="32" height="32"/><span>ThisIsMyPC</span></a><p>Free, open source, and yours forever.</p><nav aria-label="Footer"><a href={repo}>Source</a><a href={repo + "/issues"}>Support</a><a href={repo + "/blob/main/CONTRIBUTING.md"}>Contribute</a><a href={repo + "#license-and-credits"}>Credits</a></nav><label className="background-toggle"><input type="checkbox" checked={backgroundEnabled === true} disabled={backgroundEnabled === null} onChange={event => changeBackground(event.target.checked)} /><span>Animated background</span></label><span>© Sam Boland 2026</span></div></footer></div>;
}
