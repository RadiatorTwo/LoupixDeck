// Renders the GitHub releases into the changes page at deploy time, so visitors never hit the
// rate-limited GitHub API. Usage: node build-changes.mjs <page.html>
// Needs GITHUB_REPOSITORY (owner/repo) and, in CI, GITHUB_TOKEN.

import { readFile, writeFile } from "node:fs/promises";

const MARKER = /<!-- releases:start -->[\s\S]*<!-- releases:end -->/;
const repo = process.env.GITHUB_REPOSITORY || "RadiatorTwo/LoupixDeck";
const token = process.env.GITHUB_TOKEN;
const page = process.argv[2];

if (!page) {
    console.error("usage: node build-changes.mjs <page.html>");
    process.exit(1);
}

function escapeHtml(text) {
    return String(text)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;");
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

function renderRelease(release, isLatest) {
    const tag = escapeHtml(release.tag_name);
    const title = escapeHtml(release.name || release.tag_name);
    const published = new Date(release.published_at);
    const date = published.toLocaleDateString("en-US", { year: "numeric", month: "long", day: "numeric", timeZone: "UTC" });

    let badge = "";
    if (release.prerelease) badge = '<span class="badge badge-alpha">Pre-release</span>';
    else if (isLatest) badge = '<span class="badge badge-ok">Latest</span>';

    return `<article class="card release" id="${tag}">
  <header class="release-head">
    <h2><a href="#${tag}">${title}</a></h2>
    ${badge}
    <time datetime="${published.toISOString()}">${date}</time>
    <a class="release-link" href="${escapeHtml(release.html_url)}">View on GitHub</a>
  </header>
  <div class="release-body">
${release.body_html || '<p class="muted">No release notes.</p>'}
  </div>
</article>`;
}

const releases = await fetchReleases();
if (releases.length === 0) throw new Error("no published releases found");

const latest = releases.find((r) => !r.prerelease);
const html = releases.map((r) => renderRelease(r, r === latest)).join("\n");

const template = await readFile(page, "utf8");
if (!MARKER.test(template)) throw new Error(`release markers missing in ${page}`);
await writeFile(page, template.replace(MARKER, () => html), "utf8");
console.log(`Rendered ${releases.length} releases into ${page}`);
