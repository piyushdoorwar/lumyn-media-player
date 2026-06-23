import { useRef } from "react";
import { useReducedMotion, useMotionValue, useSpring } from "framer-motion";

const EASE = [0.22, 1, 0.36, 1];

export const fadeUp = {
  hidden: { opacity: 0, y: 28 },
  show: { opacity: 1, y: 0, transition: { duration: 0.7, ease: EASE } },
};

// Hero entrance cascade
export const heroContainer = {
  hidden: {},
  show: { transition: { staggerChildren: 0.13, delayChildren: 0.05 } },
};

export const heroItem = {
  hidden: { opacity: 0, y: 34 },
  show: { opacity: 1, y: 0, transition: { duration: 0.7, ease: EASE } },
};

export const heroTitle = {
  hidden: { opacity: 0, y: 42, scale: 0.96 },
  show: { opacity: 1, y: 0, scale: 1, transition: { duration: 0.9, ease: EASE } },
};

// Directional reveal: feature rows slide in from their own side toward the timeline
export const revealFrom = (x) => ({
  hidden: { opacity: 0, x },
  show: { opacity: 1, x: 0, transition: { duration: 0.7, ease: EASE } },
});

export const nodePop = {
  hidden: { scale: 0 },
  show: { scale: 1, transition: { duration: 0.5, ease: [0.34, 1.56, 0.64, 1], delay: 0.15 } },
};

// Staggered list (releases items, marquee-free lists)
export const listContainer = {
  hidden: {},
  show: { transition: { staggerChildren: 0.05 } },
};

export const listItem = {
  hidden: { opacity: 0, y: 14 },
  show: { opacity: 1, y: 0, transition: { duration: 0.4, ease: EASE } },
};

// Pointer-reactive 3D tilt. Attach onMouseMove/onMouseLeave to a container,
// `ref` to the element whose centre defines the tilt origin, and rx/ry to the
// element that should rotate.
export function useTilt(max = 7) {
  const prefersReduced = useReducedMotion();
  const ref = useRef(null);
  const rx = useSpring(0, { stiffness: 150, damping: 18 });
  const ry = useSpring(0, { stiffness: 150, damping: 18 });

  const onMouseMove = (e) => {
    if (prefersReduced) return;
    const el = ref.current;
    if (!el) return;
    const r = el.getBoundingClientRect();
    const dx = (e.clientX - (r.left + r.width / 2)) / (r.width / 2);
    const dy = (e.clientY - (r.top + r.height / 2)) / (r.height / 2);
    ry.set(dx * max);
    rx.set(-dy * max);
  };

  const onMouseLeave = () => {
    rx.set(0);
    ry.set(0);
  };

  return { ref, rx, ry, onMouseMove, onMouseLeave };
}
