import { useState } from "react";
import { motion } from "framer-motion";
import { GITHUB_URL } from "../lib/assets.js";
import { fadeUp, heroContainer, heroItem, inView } from "../motion/motion.js";
import TopBar from "../components/TopBar.jsx";
import Footer from "../components/Footer.jsx";
import SupportModal from "../components/SupportModal.jsx";
import PlayerPreview from "../components/PlayerPreview.jsx";
import Showcase from "../components/Showcase.jsx";
import DownloadSection from "../components/DownloadSection.jsx";
import Icon from "../components/Icon.jsx";

const CHECKS = [
  ["cpu", "Powered by mpv"],
  ["eyeOff", "No telemetry"],
  ["monitor", "Windows & Ubuntu"],
  ["code", "Source available"],
];

export default function LandingPage() {
  const [supportOpen, setSupportOpen] = useState(false);

  return (
    <>
      <TopBar current="home" />

      <main id="main">
        <section className="hero">
          <motion.div className="wrap hero-grid" variants={heroContainer} initial="hidden" animate="show">
            <div>
              <motion.span className="pill" variants={heroItem}>
                <b>Lumyn</b> Free for Windows &amp; Ubuntu
              </motion.span>
              <motion.h1 variants={heroItem}>
                The quiet media player for <em>your desktop.</em>
              </motion.h1>
              <motion.p className="lede" variants={heroItem}>
                Fast local video and audio playback with mpv, subtitle tools, casting and the
                controls you reach for, without the clutter. Let the media play.
              </motion.p>
              <motion.div className="actions" variants={heroItem}>
                <a className="btn btn-primary" href="#download">
                  <Icon name="download" />
                  Download Lumyn
                </a>
                <a className="btn btn-secondary" href={GITHUB_URL} rel="noreferrer">
                  <Icon name="github" />
                  View source
                </a>
              </motion.div>
              <motion.ul className="checks" variants={heroItem}>
                {CHECKS.map(([icon, label]) => (
                  <li key={label}>
                    <Icon name={icon} />
                    {label}
                  </li>
                ))}
              </motion.ul>
            </div>
            <motion.div variants={heroItem}>
              <PlayerPreview />
            </motion.div>
          </motion.div>
        </section>

        <section className="section" id="features">
          <div className="wrap">
            <motion.div className="section-head" variants={fadeUp} {...inView}>
              <p className="eyebrow">Features</p>
              <h2>Built for watching, not managing.</h2>
              <p>Everything you need while something is playing, and nothing you don&apos;t.</p>
            </motion.div>
            <Showcase />
          </div>
        </section>

        <DownloadSection />
      </main>

      <Footer onSupport={() => setSupportOpen(true)} />
      <SupportModal open={supportOpen} onClose={() => setSupportOpen(false)} />
    </>
  );
}
