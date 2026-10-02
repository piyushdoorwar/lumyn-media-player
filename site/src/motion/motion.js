const EASE = [0.22, 1, 0.36, 1];

// Section reveal on scroll
export const fadeUp = {
  hidden: { opacity: 0, y: 18 },
  show: { opacity: 1, y: 0, transition: { duration: 0.5, ease: EASE } },
};

// Hero entrance cascade
export const heroContainer = {
  hidden: {},
  show: { transition: { staggerChildren: 0.08, delayChildren: 0.05 } },
};

export const heroItem = {
  hidden: { opacity: 0, y: 16 },
  show: { opacity: 1, y: 0, transition: { duration: 0.5, ease: EASE } },
};

// Staggered lists (showcase rows, releases)
export const listContainer = {
  hidden: {},
  show: { transition: { staggerChildren: 0.05 } },
};

export const listItem = {
  hidden: { opacity: 0, y: 12 },
  show: { opacity: 1, y: 0, transition: { duration: 0.4, ease: EASE } },
};

export const inView = {
  initial: "hidden",
  whileInView: "show",
  viewport: { once: true, amount: 0.2 },
};
