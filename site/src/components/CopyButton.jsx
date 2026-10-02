import { useEffect, useRef, useState } from "react";
import Icon from "./Icon.jsx";

async function copyText(text) {
  if (navigator.clipboard?.writeText) return navigator.clipboard.writeText(text);
  // Fallback for non-secure contexts where the async clipboard API is missing.
  const area = document.createElement("textarea");
  area.value = text;
  area.setAttribute("readonly", "");
  area.style.position = "fixed";
  area.style.opacity = "0";
  document.body.appendChild(area);
  area.select();
  const ok = document.execCommand("copy");
  area.remove();
  if (!ok) throw new Error("copy failed");
}

export default function CopyButton({ text, label }) {
  const [copied, setCopied] = useState(false);
  const timer = useRef();
  useEffect(() => () => clearTimeout(timer.current), []);

  return (
    <button
      type="button"
      className={`copy-btn${copied ? " copied" : ""}`}
      aria-label={copied ? "Copied" : label}
      title={copied ? "Copied" : "Copy"}
      onClick={() =>
        copyText(text.trim()).then(
          () => {
            setCopied(true);
            clearTimeout(timer.current);
            timer.current = setTimeout(() => setCopied(false), 1600);
          },
          () => {}
        )
      }
    >
      <Icon name={copied ? "check" : "copy"} />
    </button>
  );
}
