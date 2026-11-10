"""Build data/ecdict.tsv (compact offline dictionary) from ECDICT's ecdict.csv + lemma.en.txt.

Usage: python -I tools/build_dict.py <download_dir> <out_file>
Output lines, sorted by key (lowercase, ordinal):
    key \t word \t phonetic \t translation      (translation lines joined with a literal \\n)
    key \t @lemma                                (inflected form -> dictionary form)
Only single words are kept (the hover lookup never needs phrases).
"""
import csv
import re
import sys

src, out = sys.argv[1], sys.argv[2]
csv.field_size_limit(10_000_000)
SINGLE = re.compile(r"^[A-Za-z][A-Za-z'\-.]*$")

entries = {}  # key -> list of (word, phonetic, translation, frq)
exchange = []  # (form, lemma)
with open(src + "/ecdict.csv", encoding="utf-8", newline="") as f:
    for row in csv.DictReader(f):
        word = (row["word"] or "").strip()
        tr = (row["translation"] or "").strip()
        if not word or not tr or not SINGLE.match(word) or len(word) > 40:
            continue
        lines = [l.strip() for l in tr.replace("\r", "").split("\\n") if l.strip()]
        main = [l for l in lines if not l.startswith("[网络]")]
        lines = (main or lines)[:4]
        tr = "\\n".join(l.replace("\t", " ") for l in lines)
        if len(tr) > 240:
            tr = tr[:240]
        ph = (row["phonetic"] or "").strip().replace("\t", " ")
        frq = int(row["frq"] or 0) or 10**9
        entries.setdefault(word.lower(), []).append((word, ph, tr, frq))
        for part in (row["exchange"] or "").split("/"):
            if ":" in part:
                kind, form = part.split(":", 1)
                if kind in ("p", "d", "i", "3", "r", "t", "s") and form and SINGLE.match(form):
                    exchange.append((form.lower(), word))

with open(src + "/lemma.en.txt", encoding="utf-8") as f:
    for line in f:
        if line.startswith(";") or "->" not in line:
            continue
        lemma, forms = line.split("->", 1)
        lemma = lemma.split("/")[0].strip()
        for form in forms.split(","):
            form = form.strip()
            if form and SINGLE.match(form):
                exchange.append((form.lower(), lemma))

aliases = {}
for form, lemma in exchange:
    if form in entries or form in aliases or lemma.lower() not in entries or form == lemma.lower():
        continue
    aliases[form] = lemma

rows = []
for key, lst in entries.items():
    lst.sort(key=lambda e: e[3])  # most frequent spelling first
    for word, ph, tr, _ in lst:
        rows.append(f"{key}\t{word}\t{ph}\t{tr}")
for form, lemma in aliases.items():
    rows.append(f"{form}\t@{lemma}")
rows.sort(key=lambda r: r.split("\t", 1)[0].encode("utf-8"))

with open(out, "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(rows) + "\n")
print(f"entries={len(entries)} aliases={len(aliases)} lines={len(rows)}")
