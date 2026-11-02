import json
import os
import re
import sys
import urllib.parse
import urllib.request
from datetime import datetime, timezone

REPO = os.environ.get("GITHUB_REPOSITORY", "Multron-Community/MultronWcleaner")
READMES = sys.argv[1:] or ["README.md"]
IGNORED = re.compile(r"(^update\.json(\.sig)?$)|(^SHA256SUMS)|(\.sig$)", re.IGNORECASE)

TEXT = {
    "en": {
        "badge": "Downloads",
        "summary": "**{total}** downloads across **{releases}** releases · latest [{tag}]({url}): **{count}** · updated {today}",
        "note": "Counts the installer and portable archive downloads of every release. Update checks made by the app are not counted.",
        "details": "Downloads per release",
        "header": "| Release | Date | Downloads |",
        "thousands": ",",
    },
    "tr": {
        "badge": "İndirme",
        "summary": "**{releases}** sürümde toplam **{total}** indirme · son sürüm [{tag}]({url}): **{count}** · güncelleme {today}",
        "note": "Her sürümün kurulum dosyası ve taşınabilir arşiv indirmelerini sayar. Uygulamanın yaptığı güncelleme denetimleri sayılmaz.",
        "details": "Sürüm başına indirmeler",
        "header": "| Sürüm | Tarih | İndirme |",
        "thousands": ".",
    },
}


def language(readme):
    match = re.search(r"README\.([a-z]{2})\.md$", readme, re.IGNORECASE)
    return match.group(1).lower() if match and match.group(1).lower() in TEXT else "en"


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


def replace_block(text, name, body, nl, readme):
    pattern = re.compile(rf"(<!-- {name}:start -->).*?(<!-- {name}:end -->)", re.DOTALL)
    if not pattern.search(text):
        sys.exit(f"Markers for '{name}' not found in {readme}")
    return pattern.sub(lambda m: m.group(1) + nl + body + nl + m.group(2), text)


def update(readme, rows, today):
    t = TEXT[language(readme)]

    def number(n):
        return f"{n:,}".replace(",", t["thousands"])

    total = sum(r[2] for r in rows)
    latest_tag, _, latest_count, latest_url = rows[0]

    raw = open(readme, "rb").read().decode("utf-8")
    nl = "\r\n" if "\r\n" in raw else "\n"

    badge = (
        f'<a href="#downloads">{nl}'
        f'      <img src="https://img.shields.io/badge/{urllib.parse.quote(t["badge"])}-{short(total)}-c2410c?style=for-the-badge&logo=github&logoColor=white" alt="{t["badge"]}: {number(total)}" />{nl}'
        f"    </a>"
    )

    lines = [
        t["summary"].format(total=number(total), releases=len(rows), tag=latest_tag, url=latest_url, count=number(latest_count), today=today),
        "",
        t["note"],
        "",
        "<details>",
        f"<summary>{t['details']}</summary>",
        "",
        t["header"],
        "|---|---|---:|",
    ]
    lines += [f"| [{tag}]({url}) | {date} | {number(count)} |" for tag, date, count, url in rows]
    lines += ["", "</details>"]

    text = replace_block(raw, "downloads-badge", "    " + badge, nl, readme)
    text = replace_block(text, "downloads", nl.join(lines), nl, readme)
    if text != raw:
        open(readme, "wb").write(text.encode("utf-8"))


def main():
    rows = []
    for release in fetch_releases():
        count = sum(a["download_count"] for a in release["assets"] if not IGNORED.search(a["name"]))
        rows.append((release["tag_name"], release["published_at"][:10], count, release["html_url"]))
    rows.sort(key=lambda r: r[1], reverse=True)
    today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    for readme in READMES:
        update(readme, rows, today)
    print(f"{sum(r[2] for r in rows):,} downloads across {len(rows)} releases")


if __name__ == "__main__":
    main()
