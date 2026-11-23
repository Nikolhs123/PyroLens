"""Extract Greece from a Natural Earth admin-0 GeoJSON and shrink it (4-decimal coordinates, ~11 m)."""
import json
import sys

src, dst = sys.argv[1], sys.argv[2]

with open(src, encoding="utf-8") as f:
    data = json.load(f)

matches = [ft for ft in data["features"] if ft["properties"].get("ADM0_A3") == "GRC"]
if len(matches) != 1:
    sys.exit(f"expected one GRC feature, found {len(matches)}")

feature = matches[0]
geom = feature["geometry"]
polygons = geom["coordinates"] if geom["type"] == "MultiPolygon" else [geom["coordinates"]]


def shrink(ring):
    out = []
    for x, y in ring:
        p = [round(x, 4), round(y, 4)]
        if not out or out[-1] != p:
            out.append(p)
    if out[0] != out[-1]:
        out.append(out[0])
    return out if len(out) >= 4 else None


result, points, dropped = [], 0, 0
for poly in polygons:
    rings = [shrink(r) for r in poly]
    if rings[0] is None:
        dropped += 1
        continue
    rings = [r for r in rings if r is not None]
    points += sum(len(r) for r in rings)
    result.append(rings)

xs = [p[0] for poly in result for p in poly[0]]
ys = [p[1] for poly in result for p in poly[0]]

with open(dst, "w", encoding="utf-8") as f:
    json.dump({"type": "MultiPolygon", "coordinates": result}, f, separators=(",", ":"))

props = feature["properties"]
print(f"{props.get('NAME')} ({props.get('ADM0_A3')}): {len(result)} polygons kept, {dropped} dropped, {points} points")
print(f"bbox: {min(xs)},{min(ys)},{max(xs)},{max(ys)}")
