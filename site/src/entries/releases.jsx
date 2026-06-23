import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { MotionConfig } from "framer-motion";
import "../styles/global.css";
import ReleasesPage from "../pages/ReleasesPage.jsx";

createRoot(document.getElementById("root")).render(
  <StrictMode>
    <MotionConfig reducedMotion="user">
      <ReleasesPage />
    </MotionConfig>
  </StrictMode>
);
