import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { MotionConfig } from "framer-motion";
import "../styles/global.css";
import LandingPage from "../pages/LandingPage.jsx";

createRoot(document.getElementById("root")).render(
  <StrictMode>
    <MotionConfig reducedMotion="user">
      <LandingPage />
    </MotionConfig>
  </StrictMode>
);
