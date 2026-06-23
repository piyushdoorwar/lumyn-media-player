import { useEffect } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { asset, SUPPORT_EMAIL } from "../lib/assets.js";

export default function SupportModal({ open, onClose }) {
  useEffect(() => {
    if (!open) return;
    document.body.style.overflow = "hidden";
    const onKey = (e) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => {
      document.body.style.overflow = "";
      document.removeEventListener("keydown", onKey);
    };
  }, [open, onClose]);

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className="modal-backdrop"
          role="dialog"
          aria-modal="true"
          aria-labelledby="supportModalTitle"
          onClick={(e) => {
            if (e.target === e.currentTarget) onClose();
          }}
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.2 }}
        >
          <motion.div
            className="modal-box"
            initial={{ opacity: 0, y: 24, scale: 0.97 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 16, scale: 0.98 }}
            transition={{ duration: 0.28, ease: [0.22, 1, 0.36, 1] }}
          >
            <div className="modal-header">
              <p className="eyebrow" style={{ margin: 0 }}>
                Support and issues
              </p>
              <button
                className="modal-close"
                aria-label="Close support dialog"
                onClick={onClose}
              >
                ✕
              </button>
            </div>
            <h2 id="supportModalTitle" className="modal-title">
              Need help with Lumyn?
            </h2>
            <p className="modal-body">
              Found a bug, hit a playback issue, or want to share feedback? Send an
              email and include your OS, Lumyn version, and a short note about what
              happened.
            </p>
            <a
              className="button secondary large email-link"
              href={`mailto:${SUPPORT_EMAIL}`}
            >
              <img src={asset("mail.svg")} alt="" />
              <span>{SUPPORT_EMAIL}</span>
            </a>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
