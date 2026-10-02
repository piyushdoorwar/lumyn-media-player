import { motion } from "framer-motion";
import { GITHUB_URL, SUPPORT_EMAIL } from "../lib/assets.js";
import { fadeUp, heroContainer, heroItem } from "../motion/motion.js";
import TopBar from "../components/TopBar.jsx";
import Footer from "../components/Footer.jsx";
import Icon from "../components/Icon.jsx";

const LAST_UPDATED = "May 2, 2026";

function Callout({ tone, children }) {
  return (
    <p className={`callout${tone ? ` ${tone}` : ""}`}>
      <Icon name={tone ? "alert" : "shield"} />
      <span>{children}</span>
    </p>
  );
}

const SECTIONS = [
  {
    num: "01",
    title: "Scope",
    body: (
      <p>
        This Privacy Policy applies to the Lumyn desktop application, the Lumyn website,
        release downloads, and related support channels operated by Piyush Doorwar (the
        "Developer"). By using Lumyn or the Lumyn website, you agree to this policy.
      </p>
    ),
  },
  {
    num: "02",
    title: "Plain-English Summary",
    body: (
      <>
        <p>
          Lumyn plays local media files on your device. The Developer does{" "}
          <strong>not</strong> collect your media files, watch history, subtitles,
          playlists, screenshots, settings, or playback activity from the app.
        </p>
        <Callout>
          No app accounts. No developer backend. No hidden app telemetry.
        </Callout>
      </>
    ),
  },
  {
    num: "03",
    title: "Data the App Stores Locally",
    body: (
      <>
        <p>Lumyn stores limited data on your device so the player can behave like a desktop app:</p>
        <ul>
          <li>Recent media file paths shown in the app.</li>
          <li>Resume positions and media durations, keyed by a hash of the local file path.</li>
          <li>Bookmarks and bookmark labels for local media files.</li>
          <li>Subtitle settings, including subtitle file path, font, color, size, delay, and embedded track selection.</li>
          <li>Session preferences such as volume, playback speed, and seek step.</li>
        </ul>
        <p>
          On supported desktop systems this data is stored in the operating system's
          application data area, including a Lumyn <code>settings.json</code> file. It
          remains on your device unless you share it, back it up, sync it, or delete it
          through your system.
        </p>
      </>
    ),
  },
  {
    num: "04",
    title: "Media Files and Local Playback",
    body: (
      <>
        <p>
          Lumyn opens media files and folders you choose through the file picker, drag and
          drop, command-line file association, or similar operating-system flows. Playback
          is local. The app does not upload your audio, video, cover art, playlists, or
          local subtitle files to the Developer.
        </p>
        <p>
          If you use operating-system integrations, installers, file associations, sandbox
          permissions, or desktop portals, your operating system may process file paths and
          permissions according to its own behavior.
        </p>
      </>
    ),
  },
  {
    num: "05",
    title: "Subtitle Search",
    body: (
      <>
        <p>
          Lumyn includes optional online subtitle search. When you search for subtitles, the
          app normalizes the search query, usually from a media title or filename, and sends
          that query plus selected language information directly to third-party subtitle
          providers.
        </p>
        <ul>
          <li>OpenSubtitles may receive the query, language code, request metadata, and IP address.</li>
          <li>Podnapisi may receive the query, language code, request metadata, and IP address.</li>
          <li>YTS Subtitles may receive related search or IMDb lookup requests, request metadata, and IP address.</li>
        </ul>
        <p>
          Downloaded subtitles are saved to a temporary Lumyn subtitle folder on your device.
          The Developer does not proxy, store, or monitor subtitle searches or downloads.
        </p>
        <Callout tone="warn">
          Online subtitle search sends your search query to third-party subtitle services.
        </Callout>
      </>
    ),
  },
  {
    num: "06",
    title: "Update Checks",
    body: (
      <>
        <p>
          The About dialog includes a manual "Check for Updates" action. When you use it,
          Lumyn contacts the GitHub releases API to check the latest published version.
          GitHub may receive your IP address, user agent, request time, and other standard
          request metadata.
        </p>
        <p>
          The update check is not routed through a Developer-controlled server. Lumyn does
          not send your media library, settings, watch history, or personal files as part of
          the update check.
        </p>
      </>
    ),
  },
  {
    num: "07",
    title: "Website and Release Downloads",
    body: (
      <>
        <p>
          The Lumyn website fetches public release information from GitHub so it can show
          download links. When you visit the website, download releases, open GitHub links,
          or report issues on GitHub, GitHub and your browser/network providers may receive
          standard web request data.
        </p>
        <p>
          The Lumyn website currently uses Cloudflare Web Analytics. Cloudflare may process
          limited website analytics data such as page views, referrers, device/browser
          information, approximate location derived from IP address, and similar request
          metadata. The Developer does not use the Lumyn desktop app to send analytics
          events.
        </p>
      </>
    ),
  },
  {
    num: "08",
    title: "Support, Email, and Issues",
    body: (
      <>
        <p>
          If you email support, open a GitHub issue, submit a pull request, or otherwise
          contact the Developer, you choose what information to provide. That may include
          your name, email address, GitHub username, operating system, logs, screenshots,
          media filenames, file paths, or other diagnostic details.
        </p>
        <p>
          Please avoid sending private media files, sensitive screenshots, secrets,
          passwords, API keys, or personal information that is not needed to handle your
          request.
        </p>
      </>
    ),
  },
  {
    num: "09",
    title: "What the Developer Does Not Do",
    body: (
      <ul>
        <li>The Developer does not sell your personal data.</li>
        <li>The Developer does not operate Lumyn user accounts.</li>
        <li>The Developer does not collect your media library from the app.</li>
        <li>The Developer does not track what you watch or listen to in the app.</li>
        <li>The Developer does not receive your local subtitles, bookmarks, screenshots, or playback settings from the app.</li>
        <li>The Developer does not use the desktop app for advertising, profiling, or hidden telemetry.</li>
      </ul>
    ),
  },
  {
    num: "10",
    title: "Third-Party Software and Services",
    body: (
      <>
        <p>
          Lumyn uses third-party software and services, including Avalonia, .NET, mpv/libmpv,
          GitHub, Cloudflare Web Analytics on the website, and optional subtitle providers.
          Third-party software and services are governed by their own license terms, privacy
          policies, and operational practices.
        </p>
        <p>
          The Developer is not responsible for third-party services, content, subtitle
          accuracy, service availability, security incidents, policy changes, or data
          practices outside the Developer's control.
        </p>
      </>
    ),
  },
  {
    num: "11",
    title: "Security",
    body: (
      <p>
        Lumyn is designed to keep app data local by default, but no software, operating
        system, network, storage medium, or third-party service can be guaranteed completely
        secure. You are responsible for securing your device, backups, synced folders, user
        account, downloads, and files.
      </p>
    ),
  },
  {
    num: "12",
    title: "Children's Privacy",
    body: (
      <p>
        Lumyn is a general-purpose desktop media player and is not directed to children. The
        Developer does not knowingly collect personal data from children through the Lumyn
        app.
      </p>
    ),
  },
  {
    num: "13",
    title: "License and Usage Terms",
    body: (
      <>
        <p>
          Lumyn source code is publicly viewable but is <strong>not open source</strong>{" "}
          under any OSI-approved license. The following apply:
        </p>
        <ul>
          <li>
            <strong>Permitted:</strong> view the source, download/install/use official
            releases for personal non-commercial purposes, build from the official source
            repository for personal evaluation/testing/contribution, and contribute through
            the official repository.
          </li>
          <li>
            <strong>Prohibited:</strong> copying, mirroring, redistribution, republishing,
            modification for distribution, forks or derivative works for distribution,
            repackaging, resale, sublicensing, hosting as a service, or commercial use
            without explicit written permission.
          </li>
        </ul>
        <Callout tone="restrict">
          Commercial use, redistribution, and modification for distribution are not permitted
          without explicit written permission from the copyright holder.
        </Callout>
        <p>
          Full license:{" "}
          <a href={`${GITHUB_URL}/blob/main/LICENSE`} target="_blank" rel="noopener noreferrer">
            github.com/piyushdoorwar/lumyn-media-player
          </a>
        </p>
      </>
    ),
  },
  {
    num: "14",
    title: "User Responsibility",
    body: (
      <p>
        You are responsible for how you use Lumyn, including the media you open, subtitles you
        search for or download, screenshots you create, files you save, third-party services
        you contact, and compliance with laws, copyright rules, platform terms, and
        third-party rights that apply to you.
      </p>
    ),
  },
  {
    num: "15",
    title: "No Warranty",
    body: (
      <p>
        Lumyn, the website, release downloads, subtitle search, update checks, documentation,
        and support channels are provided on an <strong>"as is"</strong> and{" "}
        <strong>"as available"</strong> basis, without warranties of any kind, whether
        express, implied, statutory, or otherwise.
      </p>
    ),
  },
  {
    num: "16",
    title: "Limitation of Liability",
    body: (
      <p>
        To the maximum extent permitted by applicable law, the Developer shall not be liable
        for any direct, indirect, incidental, consequential, special, exemplary, or punitive
        damages, including loss of data, loss of files, playback failures, subtitle errors,
        device issues, security incidents, business interruption, loss of profits, or
        reputational harm arising out of or related to Lumyn.
      </p>
    ),
  },
  {
    num: "17",
    title: "Indemnification",
    body: (
      <>
        <p>
          You agree to indemnify, defend, and hold harmless the Developer from any claims,
          damages, liabilities, losses, and expenses, including legal fees, arising out of or
          related to:
        </p>
        <ul>
          <li>Your use or misuse of Lumyn, the website, release downloads, or support channels.</li>
          <li>Your media files, subtitles, screenshots, downloads, or other content.</li>
          <li>Your violation of laws, license terms, platform rules, or third-party rights.</li>
          <li>Your use of third-party software, subtitle providers, GitHub, Cloudflare, operating-system integrations, or distribution platforms.</li>
        </ul>
      </>
    ),
  },
  {
    num: "18",
    title: "Changes to This Policy",
    body: (
      <p>
        This policy may be updated from time to time. Continued use of Lumyn or the Lumyn
        website after updates indicates acceptance of the revised policy. The "Last updated"
        date above identifies the current version.
      </p>
    ),
  },
  {
    num: "19",
    title: "Contact",
    body: (
      <p>
        Questions about this policy? Email{" "}
        <a href={`mailto:${SUPPORT_EMAIL}`}>{SUPPORT_EMAIL}</a>.
      </p>
    ),
  },
];

export default function PolicyPage() {
  return (
    <>
      <TopBar current="policy" />

      <main id="main">
        <motion.header className="page-head" variants={heroContainer} initial="hidden" animate="show">
          <div className="wrap">
            <motion.p className="eyebrow" variants={heroItem}>
              Lumyn Media Player
            </motion.p>
            <motion.h1 variants={heroItem}>Privacy Policy</motion.h1>
            <motion.p className="lede" variants={heroItem}>
              Lumyn is a local-first desktop media player. The app has no developer-run accounts,
              backend service, advertising or hidden telemetry. Some optional features contact
              third-party services when you use them.
            </motion.p>
            <motion.div className="meta" variants={heroItem}>
              <span className="badge">Last updated {LAST_UPDATED}</span>
              <span className="badge">App telemetry: none</span>
              <span className="badge warn">Third-party services: optional</span>
            </motion.div>
          </div>
        </motion.header>

        <div className="wrap">
          <div className="prose">
            {SECTIONS.map((s) => (
              <motion.section
                className="prose-card"
                key={s.num}
                aria-labelledby={`policy-${s.num}`}
                variants={fadeUp}
                initial="hidden"
                whileInView="show"
                viewport={{ once: true, amount: 0.15 }}
              >
                <h2 id={`policy-${s.num}`}>
                  <span>{s.num}</span>
                  {s.title}
                </h2>
                {s.body}
              </motion.section>
            ))}
          </div>
        </div>
      </main>

      <Footer />
    </>
  );
}
