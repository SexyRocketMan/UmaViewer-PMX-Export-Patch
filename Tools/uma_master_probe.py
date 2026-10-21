"""Probe the game's master.mdb for tables that might carry voice/audio transcription.

Read-only, throws nothing away: pass a SQL query as argv[1], or no argument to list tables.
"""
import sqlite3
import sys

MDB = r"C:\Users\user\Umamusume\umamusume_Data\Persistent\master\master.mdb"


def connect(path=MDB):
    return sqlite3.connect(f"file:{path}?mode=ro", uri=True)


def main():
    con = connect()
    if len(sys.argv) < 2:
        names = [r[0] for r in con.execute(
            "select name from sqlite_master where type='table' order by name")]
        print(f"{len(names)} tables")
        for n in names:
            print(n)
        return

    sql = sys.argv[1]
    cur = con.execute(sql)
    cols = [d[0] for d in cur.description] if cur.description else []
    print("\t".join(cols))
    n = 0
    for row in cur:
        print("\t".join("" if v is None else str(v) for v in row))
        n += 1
    print(f"-- {n} rows")


if __name__ == "__main__":
    main()
