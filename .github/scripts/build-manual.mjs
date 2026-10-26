// Renders docs/USER_MANUAL.md into the manual page at deploy time. Markdown files the manual links
// to (the CT wheel guide) are appended as appendices, so every link stays on the site.
// Usage: node build-manual.mjs <manual.md> <page.html>
// Needs GITHUB_REPOSITORY (owner/repo) and, in CI, GITHUB_TOKEN.

import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";

const CONTENT_MARKER = /<!-- manual:start -->[\s\S]*<!-- manual:end -->/;
const TOC_MARKER = /<!-- toc:start -->[\s\S]*<!-- toc:end -->/;
const repo = process.env.GITHUB_REPOSITORY || "RadiatorTwo/LoupixDeck";
const token = process.env.GITHUB_TOKEN;
const [source, page] = process.argv.slice(2);

if (!source || !page) {
    console.error("usage: node build-manual.mjs <manual.md> <page.html>");
    process.exit(1);
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

// GitHub's heading slugs, so the manual's own #anchors keep working.
function slugify(html) {
    return decodeEntities(html.replace(/<[^>]*>/g, ""))
        .trim()
        .toLowerCase()
        .replace(/[^\p{L}\p{M}\p{N}\p{Pc}\- ]/gu, "")
        .replace(/ /g, "-");
}

// The Markdown API renders exactly like github.com and sanitizes the output.
async function renderMarkdown(text) {
    const headers = {
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "loupixdeck-site",
    };
    if (token) headers.Authorization = `Bearer ${token}`;

    const response = await fetch("https://api.github.com/markdown", {
        method: "POST",
        headers,
        body: JSON.stringify({ text, mode: "gfm", context: repo }),
    });
    if (!response.ok) throw new Error(`GitHub Markdown API returned HTTP ${response.status}`);
    return response.text();
}

// Appendices sit below the manual's own h2 sections, so their headings move down one level.
function demoteHeadings(markdown) {
    return markdown.replace(/^(#{1,5}) /gm, "#$1 ");
}

function firstHeading(markdown, file) {
    const match = /^# (.+)$/m.exec(markdown);
    if (!match) throw new Error(`${file} has no top-level heading`);
    return match[1].trim();
}

let manual = await readFile(source, "utf8");

const linked = [...new Set([...manual.matchAll(/\]\(([^)#:\s]+\.md)(#[^)]*)?\)/g)].map((m) => m[1]))];
const appendices = [];
for (const file of linked) {
    const markdown = await readFile(path.join(path.dirname(source), file), "utf8");
    const anchor = slugify(firstHeading(markdown, file));
    appendices.push(demoteHeadings(markdown));
    manual = manual.replaceAll(`](${file})`, `](#${anchor})`);
}

let html = await renderMarkdown([manual, ...appendices].join("\n\n"));

const used = new Map();
const toc = [];
html = html.replace(/<h([1-6])([^>]*)>([\s\S]*?)<\/h\1>/g, (_, level, attrs, inner) => {
    const base = slugify(inner);
    const count = used.get(base) || 0;
    used.set(base, count + 1);
    const id = count === 0 ? base : `${base}-${count}`;
    if (level === "2") toc.push(`<a href="#${id}">${inner}</a>`);
    return `<h${level}${attrs} id="${id}">${inner}</h${level}>`;
});

// Wide reference tables scroll inside their own box instead of widening the page.
html = html.replace(/<table[\s\S]*?<\/table>/g, (table) => `<div class="table-box">${table}</div>`);

const ids = new Set([...html.matchAll(/ id="([^"]+)"/g)].map((m) => m[1]));
for (const [, target] of html.matchAll(/href="#([^"]+)"/g)) {
    if (!ids.has(decodeURIComponent(target))) console.warn(`::warning::Manual link #${target} has no matching heading`);
}

const template = await readFile(page, "utf8");
if (!CONTENT_MARKER.test(template) || !TOC_MARKER.test(template)) throw new Error(`manual markers missing in ${page}`);
const output = template
    .replace(TOC_MARKER, () => toc.join("\n"))
    .replace(CONTENT_MARKER, () => html);
await writeFile(page, output, "utf8");
console.log(`Rendered ${source} (${appendices.length} appendices, ${toc.length} sections) into ${page}`);
