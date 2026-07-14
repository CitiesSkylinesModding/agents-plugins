#!/usr/bin/env node

/* oxlint-disable no-console, typescript/no-unsafe-call, typescript/no-unsafe-member-access, typescript/no-unsafe-assignment, typescript/no-unsafe-argument -- standalone node CLI shipped with the skill; console IS the output, and node globals resolve as error types outside the workspace tsconfig. */

// Fetch a docs.coherent-labs.com page and print its <main> content as rough markdown.
// The docs site front-loads hundreds of KB of minified navigation on every page, which starves
// summarizing fetch tools (they never reach the content); this extractor skips straight to it.
//
// Usage:
//   node fetch-doc.mjs <url> [outfile]   Print a doc page's content (or write it to outfile).
//   node fetch-doc.mjs sitemap [outfile] List every page URL of the Gameface docs site.

import { writeFile } from 'node:fs/promises';

const SITEMAP_URL = 'https://docs.coherent-labs.com/cpp-gameface/sitemap.xml';

const [target, outfile] = process.argv.slice(2);

if (!target) {
  console.error(`usage: node fetch-doc.mjs <url>|sitemap [outfile]`);
  process.exit(1);
}

if (target == 'sitemap') {
  const xml = await fetchText(SITEMAP_URL);

  // The site's sitemap emits site-relative <loc> paths; absolutize them for direct reuse.
  const urls = [...xml.matchAll(/<loc>(?<loc>[^<]+)<\/loc>/gu)].map(match =>
    match.groups.loc.startsWith('/')
      ? `https://docs.coherent-labs.com${match.groups.loc}`
      : match.groups.loc
  );

  await output(urls.join('\n'));
} else {
  const html = await fetchText(target);

  await output(htmlToMarkdown(html));
}

/**
 * @param {string} url The absolute URL to fetch.
 */
async function fetchText(url) {
  const response = await fetch(url);

  if (!response.ok) {
    console.error(`HTTP ${response.status} for ${url}`);
    process.exit(2);
  }

  return response.text();
}

/**
 * @param {string} html The raw page HTML.
 */
function htmlToMarkdown(html) {
  // Keep only the page content; everything outside <main> is navigation chrome.
  const mainStart = html.indexOf('<main');
  const mainEnd = html.lastIndexOf('</main>');
  const main = mainStart !== -1 && mainEnd > mainStart ? html.slice(mainStart, mainEnd) : html;

  return `${main
    .replaceAll(/<script[\s\S]*?<\/script>/giu, '')
    .replaceAll(/<style[\s\S]*?<\/style>/giu, '')
    .replaceAll(/<h1[^>]*>(?<text>[\s\S]*?)<\/h1>/giu, '\n# $<text>\n')
    .replaceAll(/<h2[^>]*>(?<text>[\s\S]*?)<\/h2>/giu, '\n## $<text>\n')
    .replaceAll(/<h3[^>]*>(?<text>[\s\S]*?)<\/h3>/giu, '\n### $<text>\n')
    .replaceAll(/<h4[^>]*>(?<text>[\s\S]*?)<\/h4>/giu, '\n#### $<text>\n')
    .replaceAll(/<li[^>]*>/giu, '\n- ')
    .replaceAll(/<\/li>/giu, '')
    // Tables become pipe-separated rows; good enough to read the support tables.
    .replaceAll(/<tr[^>]*>/giu, '\n| ')
    .replaceAll(/<\/t[dh]>\s*<t[dh][^>]*>/giu, ' | ')
    .replaceAll(/<\/tr>/giu, ' |')
    .replaceAll(/<t[dh][^>]*>/giu, '')
    .replaceAll(/<br\s*\/?>/giu, '\n')
    .replaceAll(
      /<\/?(?<tag>p|div|section|table|thead|tbody|ul|ol|pre|blockquote|article|nav|main|figure)[^>]*>/giu,
      '\n'
    )
    .replaceAll(/<code[^>]*>/giu, '`')
    .replaceAll(/<\/code>/giu, '`')
    .replaceAll(
      /<a [^>]*href="(?<href>[^"]*)"[^>]*>(?<label>[\s\S]*?)<\/a>/giu,
      '[$<label>]($<href>)'
    )
    .replaceAll(/<[^>]+>/gu, '')
    .replaceAll('&lt;', '<')
    .replaceAll('&gt;', '>')
    .replaceAll('&amp;', '&')
    .replaceAll('&quot;', '"')
    .replaceAll(/&#39;|&apos;/gu, "'")
    .replaceAll('&nbsp;', ' ')
    .replaceAll(/[ \t]+\n/gu, '\n')
    .replaceAll(/\n{3,}/gu, '\n\n')
    .trim()}\n`;
}

/**
 * @param {string} text The extracted content to print or write.
 */
async function output(text) {
  if (outfile) {
    await writeFile(outfile, text);
    console.log(`${outfile}: ${text.length} chars`);
  } else {
    console.log(text);
  }
}
