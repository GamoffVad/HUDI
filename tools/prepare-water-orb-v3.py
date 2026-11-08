"""More visible inner water flow; reuse the approved round v2 base and rim."""
import importlib.util
from pathlib import Path

spec = importlib.util.spec_from_file_location('water_orb', Path(__file__).with_name('prepare-water-orb-v2.py'))
water = importlib.util.module_from_spec(spec)
spec.loader.exec_module(water)
water.VERSION = 3
water.DEST = water.ROOT / 'assets/water-orb/v3'
water.AMPLITUDE = 14.0
water.SECONDS = 5
water.COUNT = water.FPS * water.SECONDS

if __name__ == '__main__':
    water.main()
