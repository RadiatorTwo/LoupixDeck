// Renders the GitHub releases into the changes section at deploy time, so visitors never hit the
// rate-limited GitHub API: an overview at <dir>/index.html and one page per release at
// <dir>/<slug>/index.html (v1.48.0 -> v1-48-0), built from <dir>/_release.html.
// With a home page, its SoftwareApplication JSON-LD also gets the latest version.
// Usage: node build-changes.mjs <changes-dir> [home.html]
// Needs GITHUB_REPOSITORY (owner/repo) and, in CI, GITHUB_TOKEN.

import { mkdir, readFile, rm, writeFile } from "node:fs/promises";
import path from "node:path";

const LIST_MARKER = /<!-- releases:start -->[\s\S]*<!-- releases:end -->/;
const PAGE_MARKER = /<!-- release:start -->[\s\S]*<!-- release:end -->/;
const JSON_LD = /(<script type="application\/ld\+json">)([\s\S]*?)(<\/script>)/;
const SUMMARY_LENGTH = 180;
const repo = process.env.GITHUB_REPOSITORY || "RadiatorTwo/LoupixDeck";
const token = process.env.GITHUB_TOKEN;
const [dir, home] = process.argv.slice(2);

if (!dir) {
    console.error("usage: node build-changes.mjs <changes-dir>");
    process.exit(1);
}

function escapeHtml(text) {
    return String(text)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;");
}

function decodeEntities(text) {
    return text
        .replace(/&#x([0-9a-f]+);/gi, (_, hex) => String.fromCodePoint(parseInt(hex, 16)))
        .replace(/&#(\d+);/g, (_, dec) => String.fromCodePoint(Number(dec)))
        .replaceAll("&quot;", '"')
        .replaceAll("&lt;", "<")
        .replaceAll("&gt;", ">")
        .replaceAll("&amp;", "&");
}

function plainText(html) {
    return decodeEntities(html.replace(/<[^>]*>/g, " ")).replace(/\s+/g, " ").trim();
}

function slugify(tag) {
    return tag.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}

// body_html is GitHub's own rendering of the release Markdown, already sanitized.
async function fetchReleases() {
    const headers = {
        "Accept": "application/vnd.github.html+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "loupixdeck-site",
    };
    if (token) headers.Authorization = `Bearer ${token}`;

    const releases = [];
    for (let pageNo = 1; ; pageNo++) {
        const response = await fetch(`https://api.github.com/repos/${repo}/releases?per_page=100&page=${pageNo}`, { headers });
        if (!response.ok) throw new Error(`GitHub API returned HTTP ${response.status}`);
        const batch = await response.json();
        releases.push(...batch);
        if (batch.length < 100) break;
    }

    return releases
        .filter((r) => !r.draft && r.published_at)
        .sort((a, b) => Date.parse(b.published_at) - Date.parse(a.published_at));
}

// The section headings say what a release is about; releases without them get their first words.
function summarize(release) {
    const body = release.body_html || "";
    const topics = [...body.matchAll(/<h3[^>]*>([\s\S]*?)<\/h3>/g)].map((m) => plainText(m[1])).filter(Boolean);
    if (topics.length > 0) return { topics, text: topics.join(" · ") };

    const text = body
        .replace(/<h[1-6][^>]*>[\s\S]*?<\/h[1-6]>/g, "")
        .split(/<\/(?:li|p)>|<br\s*\/?>/)
        .map(plainText)
        .filter(Boolean)
        .join(" · ");
    if (text.length <= SUMMARY_LENGTH) return { topics, text };
    return { topics, text: `${text.slice(0, SUMMARY_LENGTH).replace(/\s+\S*$/, "")}…` };
}

function formatDate(release) {
    const published = new Date(release.published_at);
    return {
        iso: published.toISOString(),
        text: published.toLocaleDateString("en-US", { year: "numeric", month: "long", day: "numeric", timeZone: "UTC" }),
    };
}

function badge(release, latest) {
    if (release.prerelease) return '<span class="badge badge-alpha">Pre-release</span>';
    if (release === latest) return '<span class="badge badge-ok">Latest</span>';
    return "";
}

function renderListItem(release, latest) {
    const date = formatDate(release);
    const { text } = summarize(release);
    return `<a class="card release-item" href="${slugify(release.tag_name)}/">
  <span class="release-meta">
    <span class="release-version">${escapeHtml(release.name || release.tag_name)}</span>
    ${badge(release, latest)}
    <time datetime="${date.iso}">${date.text}</time>
  </span>
  <span class="release-summary">${escapeHtml(text || "Maintenance release.")}</span>
</a>`;
}

function formatSize(bytes) {
    return bytes >= 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

// Direct file links, so a download never detours through the GitHub release page.
function renderDownloads(release) {
    const assets = (release.assets || []).filter((a) => a.state === "uploaded");
    if (assets.length === 0) return "";

    const rows = assets
        .sort((a, b) => a.name.localeCompare(b.name, "en"))
        .map((a) => `<li><a href="${escapeHtml(a.browser_download_url)}"><span class="mdi" aria-hidden="true">&#xF01DA;</span>${escapeHtml(a.name)}</a><span class="muted">${formatSize(a.size)}</span></li>`)
        .join("\n    ");
    return `<section class="release-downloads" id="downloads" aria-labelledby="downloads-title">
  <h2 id="downloads-title" class="eyebrow">Downloads</h2>
  <ul>
    ${rows}
  </ul>
</section>`;
}

function renderPage(release, latest, newer, older) {
    const date = formatDate(release);
    const downloads = renderDownloads(release);
    const step = (other, label, cls) => other
        ? `<a class="card release-step ${cls}" href="../${slugify(other.tag_name)}/"><span class="eyebrow">${label}</span><span>${escapeHtml(other.name || other.tag_name)}</span></a>`
        : "<span></span>";

    return `<a class="back-link" href="../"><span class="mdi" aria-hidden="true">&#xF004D;</span>All releases</a>
<article class="card release">
  <header class="release-head">
    <h1>${escapeHtml(release.name || release.tag_name)}</h1>
    ${badge(release, latest)}
    <time datetime="${date.iso}">${date.text}</time>
    ${downloads ? '<a class="btn btn-primary release-download" href="#downloads"><span class="mdi" aria-hidden="true">&#xF01DA;</span>Downloads</a>' : ""}
  </header>
  <div class="release-body">
${release.body_html || '<p class="muted">No release notes.</p>'}
  </div>
  ${downloads}
</article>
<nav class="release-steps" aria-label="Other releases">
  ${step(older, "Older", "older")}
  ${step(newer, "Newer", "newer")}
</nav>`;
}

const releases = await fetchReleases();
if (releases.length === 0) throw new Error("no published releases found");
const latest = releases.find((r) => !r.prerelease);

const listPath = path.join(dir, "index.html");
const templatePath = path.join(dir, "_release.html");
const list = await readFile(listPath, "utf8");
const template = await readFile(templatePath, "utf8");
if (!LIST_MARKER.test(list)) throw new Error(`release markers missing in ${listPath}`);
if (!PAGE_MARKER.test(template)) throw new Error(`release markers missing in ${templatePath}`);

const seen = new Set();
for (const [i, release] of releases.entries()) {
    const slug = slugify(release.tag_name);
    if (!slug || seen.has(slug)) throw new Error(`release ${release.tag_name} has no unique page name`);
    seen.add(slug);

    const name = release.name || release.tag_name;
    const description = summarize(release).text || `Release notes for LoupixDeck ${name}.`;
    const html = template
        .replace(PAGE_MARKER, () => renderPage(release, latest, releases[i - 1], releases[i + 1]))
        .replaceAll("{{title}}", () => escapeHtml(`${name} release notes`))
        .replaceAll("{{description}}", () => escapeHtml(description));

    await mkdir(path.join(dir, slug), { recursive: true });
    await writeFile(path.join(dir, slug, "index.html"), html, "utf8");
}

const items = releases.map((r) => renderListItem(r, latest)).join("\n");
await writeFile(listPath, list.replace(LIST_MARKER, () => items), "utf8");
await rm(templatePath);
console.log(`Rendered ${releases.length} release pages into ${dir}`);

if (home) {
    const page = await readFile(home, "utf8");
    const match = JSON_LD.exec(page);
    if (!match) throw new Error(`JSON-LD block missing in ${home}`);

    const data = JSON.parse(match[2]);
    data.softwareVersion = latest.tag_name.replace(/^v/, "");
    data.releaseNotes = new URL(`changes/${slugify(latest.tag_name)}/`, data.url).href;
    data.dateModified = latest.published_at.slice(0, 10);
    const json = JSON.stringify(data, null, 2).replaceAll("<", "\\u003c");

    await writeFile(home, page.replace(JSON_LD, (_, open, __, close) => `${open}\n${json}\n${close}`), "utf8");
    console.log(`Set ${home} JSON-LD to version ${data.softwareVersion}`);
}
