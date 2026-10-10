import { motion } from "framer-motion";
import { BASE } from "../lib/assets.js";
import { heroContainer, heroItem } from "../motion/motion.js";
import TopBar from "../components/TopBar.jsx";
import Footer from "../components/Footer.jsx";
import Icon from "../components/Icon.jsx";

// GitHub Pages serves this page at any missing path, so every link uses BASE
// (root-absolute, "/" on the lumyn.piyushdoorwar.com subdomain).
export default function NotFoundPage() {
  return (
    <>
      <TopBar />

      <main id="main">
        <motion.section className="notfound" variants={heroContainer} initial="hidden" animate="show">
          <div className="wrap">
            <motion.div className="code404" variants={heroItem}>
              404
            </motion.div>
            <motion.h1 variants={heroItem}>Nothing is playing at this address.</motion.h1>
            <motion.p variants={heroItem}>
              The link may be old, or the page may have moved. Try one of these instead.
            </motion.p>
            <motion.div className="actions" variants={heroItem}>
              <a className="btn btn-primary" href={BASE}>
                <span>Go to the home page</span>
                <Icon name="arrowRight" />
              </a>
              <a className="btn btn-secondary" href={`${BASE}releases/`}>
                <Icon name="tag" />
                <span>Releases</span>
              </a>
            </motion.div>
          </div>
        </motion.section>
      </main>

      <Footer />
    </>
  );
}
