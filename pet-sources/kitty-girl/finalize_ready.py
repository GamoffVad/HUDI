"""Finalize each clip only after this revision's native matte is complete."""
import sys,time
from finalize import ROOT,run
cutoff=(ROOT/'animations/look-around/frames/0000.png').stat().st_mtime
for name in sys.argv[1:]:
    audit=ROOT/'animations'/name/'matte-audit.json'
    deadline=time.monotonic()+1800
    while not(audit.exists() and audit.stat().st_mtime>cutoff):
        if time.monotonic()>deadline:raise TimeoutError(name)
        time.sleep(2)
    run(name)
