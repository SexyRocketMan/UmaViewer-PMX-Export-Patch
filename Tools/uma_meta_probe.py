"""Probe a meta database (the game's asset index) - the plain copy in the repo, by default.

Usage: python Tools/uma_meta_probe.py            -> schema
       python Tools/uma_meta_probe.py "<sql>"
"""
import os
import sqlite3
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
META = os.path.join(ROOT, "meta_plain.sqlite")


def main():
    con = sqlite3.connect(f"file:{META}?mode=ro", uri=True)
    if len(sys.argv) < 2:
        for row in con.execute("select type, name, sql from sqlite_master"):
            print(row[0], "|", row[1], "|", (row[2] or "")[:300])
        return
    cur = con.execute(sys.argv[1])
    cols = [d[0] for d in cur.description] if cur.description else []
    print("\t".join(cols))
    n = 0
    for row in cur:
        print("\t".join("" if v is None else str(v) for v in row))
        n += 1
    print(f"-- {n} rows")


if __name__ == "__main__":
    main()
