import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { MotionConfig } from "framer-motion";
import "../styles/global.css";
import "../lib/imageGuard.js";
import PolicyPage from "../pages/PolicyPage.jsx";

createRoot(document.getElementById("root")).render(
  <StrictMode>
    <MotionConfig reducedMotion="user">
      <PolicyPage />
    </MotionConfig>
  </StrictMode>
);
