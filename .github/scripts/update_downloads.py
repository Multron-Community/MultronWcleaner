import json
import os
import re
import sys
import urllib.request
from datetime import datetime, timezone

REPO = os.environ.get("GITHUB_REPOSITORY", "Multron-Community/MultronWcleaner")
README = sys.argv[1] if len(sys.argv) > 1 else "README.md"
IGNORED = re.compile(r"(^update\.json(\.sig)?$)|(^SHA256SUMS)|(\.sig$)", re.IGNORECASE)


def fetch_releases():
    releases, page = [], 1
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "mwc-download-stats"}
    token = os.environ.get("GITHUB_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    while True:
        url = f"https://api.github.com/repos/{REPO}/releases?per_page=100&page={page}"
        with urllib.request.urlopen(urllib.request.Request(url, headers=headers)) as response:
            batch = json.load(response)
        if not batch:
            return releases
        releases.extend(r for r in batch if not r.get("draft"))
        page += 1


def short(n):
    if n >= 1_000_000:
        return f"{n / 1_000_000:.1f}M".replace(".0M", "M")
    if n >= 1_000:
        return f"{n / 1_000:.1f}k".replace(".0k", "k")
    return str(n)


def replace_block(text, name, body, nl):
    pattern = re.compile(rf"(<!-- {name}:start -->).*?(<!-- {name}:end -->)", re.DOTALL)
    if not pattern.search(text):
        sys.exit(f"Markers for '{name}' not found in {README}")
    return pattern.sub(lambda m: m.group(1) + nl + body + nl + m.group(2), text)


def main():
    rows = []
    for release in fetch_releases():
        count = sum(a["download_count"] for a in release["assets"] if not IGNORED.search(a["name"]))
        rows.append((release["tag_name"], release["published_at"][:10], count, release["html_url"]))
    rows.sort(key=lambda r: r[1], reverse=True)
    total = sum(r[2] for r in rows)
    latest_tag, _, latest_count, latest_url = rows[0]
    today = datetime.now(timezone.utc).strftime("%Y-%m-%d")

    raw = open(README, "rb").read().decode("utf-8")
    nl = "\r\n" if "\r\n" in raw else "\n"

    badge = (
        f'<a href="#downloads">{nl}'
        f'      <img src="https://img.shields.io/badge/Downloads-{short(total)}-c2410c?style=for-the-badge&logo=github&logoColor=white" alt="Downloads: {total:,}" />{nl}'
        f"    </a>"
    )

    lines = [
        f"**{total:,}** downloads across **{len(rows)}** releases · latest [{latest_tag}]({latest_url}): **{latest_count:,}** · updated {today}",
        "",
        "Counts the installer and portable archive downloads of every release. Update checks made by the app are not counted.",
        "",
        "<details>",
        "<summary>Downloads per release</summary>",
        "",
        "| Release | Date | Downloads |",
        "|---|---|---:|",
    ]
    lines += [f"| [{tag}]({url}) | {date} | {count:,} |" for tag, date, count, url in rows]
    lines += ["", "</details>"]

    text = replace_block(raw, "downloads-badge", "    " + badge, nl)
    text = replace_block(text, "downloads", nl.join(lines), nl)
    if text != raw:
        open(README, "wb").write(text.encode("utf-8"))
    print(f"{total:,} downloads across {len(rows)} releases")


if __name__ == "__main__":
    main()
