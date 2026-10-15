import type { Metadata } from "next";
import "./materials.css";
import "./site.css";
export const metadata: Metadata = { icons: { icon: "/icon.png", apple: "/icon.png" }, metadataBase: new URL("https://thisismypc.com"), title: "ThisIsMyPC. Take control of your PC.", description: "A free, open-source Windows app for system controls, privacy settings, apps, and hardware. Inspect changes before you apply them." };
export default function Layout({ children }: {
    children: React.ReactNode;
}) { return <html lang="en"><body>{children}</body></html>; }
