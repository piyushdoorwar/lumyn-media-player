import { useEffect, useRef } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { SUPPORT_EMAIL } from "../lib/assets.js";
import Icon from "./Icon.jsx";

export default function SupportModal({ open, onClose }) {
  const closeRef = useRef(null);

  useEffect(() => {
    if (!open) return;
    const previous = document.activeElement;
    document.body.style.overflow = "hidden";
    closeRef.current?.focus();
    const onKey = (e) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => {
      document.body.style.overflow = "";
      document.removeEventListener("keydown", onKey);
      previous?.focus?.();
    };
  }, [open, onClose]);

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className="modal-backdrop"
          onClick={(e) => {
            if (e.target === e.currentTarget) onClose();
          }}
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.18 }}
        >
          <motion.div
            className="modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="support-title"
            initial={{ opacity: 0, y: 12, scale: 0.98 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 8, scale: 0.98 }}
            transition={{ duration: 0.22, ease: [0.22, 1, 0.36, 1] }}
          >
            <div className="modal-head">
              <p className="eyebrow">Support</p>
              <button
                ref={closeRef}
                type="button"
                className="icon-btn"
                aria-label="Close support dialog"
                onClick={onClose}
              >
                <Icon name="close" />
              </button>
            </div>
            <h2 id="support-title">Need help with Lumyn?</h2>
            <p>
              Found a bug, hit a playback issue, or want to share feedback? Send an email with your
              OS, Lumyn version, and a short note about what happened.
            </p>
            <a className="btn btn-secondary" href={`mailto:${SUPPORT_EMAIL}`}>
              <Icon name="mail" />
              <span>{SUPPORT_EMAIL}</span>
            </a>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
