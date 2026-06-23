import { useState } from "react";

export default function CopyButton({ text, label }) {
  const [copied, setCopied] = useState(false);
  return (
    <button
      className="copy-btn"
      aria-label={label}
      onClick={() => {
        navigator.clipboard.writeText(text.trim()).then(() => {
          setCopied(true);
          setTimeout(() => setCopied(false), 1800);
        });
      }}
    >
      {copied ? "Copied!" : "Copy"}
    </button>
  );
}
