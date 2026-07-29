#!/usr/bin/env python3
"""Генератор тестового CSV для POST /api/ingest/csv.

Формат: timestamp,lon,lat,speed,heading
Пример:
    python3 scripts/generate_sample_csv.py --rows 1000000 --out track.csv
    curl -X POST "http://localhost:8080/api/ingest/csv?objectId=<guid>" \
         -H "Content-Type: text/csv" --data-binary @track.csv
"""
import argparse
import math
import random
from datetime import datetime, timedelta, timezone


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--rows", type=int, default=1_000_000, help="число точек (default: 1 000 000)")
    ap.add_argument("--out", default="track.csv", help="выходной файл")
    ap.add_argument("--center-lat", type=float, default=55.75)
    ap.add_argument("--center-lon", type=float, default=37.62)
    ap.add_argument("--step-seconds", type=int, default=1)
    args = ap.parse_args()

    random.seed(42)
    lat, lon = args.center_lat, args.center_lon
    heading = random.uniform(0, 360)
    ts = datetime.now(timezone.utc) - timedelta(seconds=args.rows * args.step_seconds)

    with open(args.out, "w", encoding="utf-8") as f:
        f.write("timestamp,lon,lat,speed,heading\n")
        for _ in range(args.rows):
            heading += random.uniform(-30, 30)
            dist = random.uniform(10, 60)  # метров за шаг
            lat += (dist * math.cos(math.radians(heading))) / 111_320
            lon += (dist * math.sin(math.radians(heading))) / (111_320 * math.cos(math.radians(lat)))
            ts += timedelta(seconds=args.step_seconds)
            f.write(
                f"{ts.isoformat()},{lon:.6f},{lat:.6f},{dist / args.step_seconds:.1f},{heading % 360:.1f}\n"
            )

    print(f"OK: {args.rows} строк -> {args.out}")


if __name__ == "__main__":
    main()
