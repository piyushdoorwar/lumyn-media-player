import { useState } from "react";
import { motion } from "framer-motion";
import { asset, GITHUB_URL } from "../lib/assets.js";
import {
  heroContainer,
  heroItem,
  heroTitle,
  fadeUp,
  useTilt,
} from "../motion/motion.js";
import TopBar from "../components/TopBar.jsx";
import Footer from "../components/Footer.jsx";
import SupportModal from "../components/SupportModal.jsx";
import ScrollProgress from "../components/ScrollProgress.jsx";
import PlayerPreview from "../components/PlayerPreview.jsx";
import Marquee from "../components/Marquee.jsx";
import FeatureTimeline from "../components/FeatureTimeline.jsx";
import DownloadSection from "../components/DownloadSection.jsx";

export default function LandingPage() {
  const [supportOpen, setSupportOpen] = useState(false);
  const tilt = useTilt(7);

  return (
    <>
      <ScrollProgress />
      <TopBar current="home" />

      <main id="top">
        <motion.section
          className="hero"
          variants={heroContainer}
          initial="hidden"
          animate="show"
          onMouseMove={tilt.onMouseMove}
          onMouseLeave={tilt.onMouseLeave}
        >
          <motion.div className="hero-copy" variants={heroContainer}>
            <motion.p className="eyebrow" variants={heroItem}>
              Desktop media player
            </motion.p>
            <motion.h1 variants={heroTitle}>Lumyn</motion.h1>
            <motion.p className="lede" variants={heroItem}>
              A quiet, fast local video player with a focused interface, mpv playback,
              subtitle tools, and the controls you expect close at hand.
            </motion.p>
            <motion.div className="actions" variants={heroItem}>
              <motion.a
                className="button primary icon-link"
                href="#download"
                whileHover={{ y: -2 }}
                whileTap={{ scale: 0.97 }}
              >
                <img src={asset("download.svg")} alt="" />
                <span>Download latest</span>
              </motion.a>
              <motion.a
                className="button secondary github-link"
                href={GITHUB_URL}
                rel="noreferrer"
                whileHover={{ y: -2 }}
                whileTap={{ scale: 0.97 }}
              >
                <img src={asset("github.svg")} alt="" />
                <span>View source</span>
              </motion.a>
            </motion.div>
          </motion.div>

          <motion.div className="preview-tilt" variants={heroItem} ref={tilt.ref}>
            <motion.div
              className="preview-tilt-inner"
              style={{ rotateX: tilt.rx, rotateY: tilt.ry }}
            >
              <PlayerPreview />
            </motion.div>
          </motion.div>

          <Marquee />
        </motion.section>

        <section className="band" id="features">
          <motion.div
            className="section-heading"
            variants={fadeUp}
            initial="hidden"
            whileInView="show"
            viewport={{ once: true, amount: 0.5 }}
          >
            <p className="eyebrow">What it does</p>
            <h2>Built for watching, not managing.</h2>
          </motion.div>
          <FeatureTimeline />
        </section>

        <DownloadSection />
      </main>

      <Footer variant="home" onSupport={() => setSupportOpen(true)} />
      <SupportModal open={supportOpen} onClose={() => setSupportOpen(false)} />
    </>
  );
}
