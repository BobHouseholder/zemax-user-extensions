"""Orientation for the retardance controls.

Two things nobody has written down:

  1. the lens prescription, so a CLOSED-FORM retardance can be computed for a
     uniform stress field (retardance = dn * local thickness);
  2. what GetRetardanceMap's seven arguments actually ARE. Runner.cs calls it
     as (8, 0, 1, 1.0, 0.0, 0.0, 0.0) and documents only that the first is a
     "sampling SELECTOR, not a point count". If one of the other six is a
     WAVELENGTH or a DIRECTION, the shipped peak is quoted over a domain
     nobody chose.

Writes nothing. Prints.
"""
import os
from zos import ZOSAPI, connect, HERE

BASE = os.path.join(HERE, "plastic-cooke-MoldStress.zmx")

app = connect()
s = app.PrimarySystem
assert s.LoadFile(BASE, False)

print("=" * 78)
print("LENS PRESCRIPTION  %s" % os.path.basename(BASE))
print("=" * 78)
print("%-4s %-12s %12s %10s %10s %10s" %
      ("surf", "material", "radius", "thick", "semi-dia", "conic"))
n = s.LDE.NumberOfSurfaces
for i in range(n):
    row = s.LDE.GetSurfaceAt(i)
    mat = row.Material or ""
    try:
        r = row.Radius
    except Exception:
        r = float("nan")
    print("%-4d %-12s %12.5f %10.5f %10.5f %10.4f" %
          (i, mat, r, row.Thickness, row.SemiDiameter, row.Conic))

print()
print("wavelengths:")
for w in range(1, s.SystemData.Wavelengths.NumberOfWavelengths + 1):
    ww = s.SystemData.Wavelengths.GetWavelength(w)
    try:
        prim = bool(ww.IsPrimary)
    except Exception:
        prim = False
    print("   %d  %.7f um   weight %.3f%s" %
          (w, ww.Wavelength, ww.Weight, "   PRIMARY" if prim else ""))
print("fields:")
for f in range(1, s.SystemData.Fields.NumberOfFields + 1):
    ff = s.SystemData.Fields.GetField(f)
    print("   %d  X %.4f  Y %.4f" % (f, ff.X, ff.Y))
print("aperture: type=%s value=%.5f" %
      (s.SystemData.Aperture.ApertureType, s.SystemData.Aperture.ApertureValue))

# ---------------------------------------------------------------- signature
print()
print("=" * 78)
print("GetRetardanceMap SIGNATURE")
print("=" * 78)
st = s.LDE.GetSurfaceAt(1).STARData.Stress
fits = st.Fits
# pythonnet synthesises a __doc__ carrying every overload's real parameter
# names and types. That is the authoritative signature; .NET reflection through
# the COM wrapper is not.
found = False
for name in sorted(dir(fits)):
    if "Retardance" in name or "Birefring" in name or "Stress" in name:
        found = True
        doc = getattr(type(fits), name, None)
        doc = getattr(doc, "__doc__", None) or getattr(getattr(fits, name), "__doc__", "")
        print("  %s" % name)
        for line in (doc or "<no doc>").splitlines():
            if line.strip():
                print("        %s" % line.strip())
if not found:
    print("  NOTHING matched on", type(fits))
print()
print("  all members of Fits:")
print("   ", ", ".join(n for n in sorted(dir(fits)) if not n.startswith("_")))
print()
print("  all members of STARData.Stress:")
print("   ", ", ".join(n for n in sorted(dir(st)) if not n.startswith("_")))

# and what a returned point carries
print()
print("RETARDANCE POINT FIELDS")
try:
    mp = fits.GetRetardanceMap(8, 0, 1, 1.0, 0.0, 0.0, 0.0)
    print("  map on a STRESS-FREE surface:",
          "None" if mp is None else "%d points" % len(mp))
    if mp is not None and len(mp):
        pt = mp[0]
        pt_t = pt.GetType()
        print("  point type:", pt_t.FullName)
        for pr in pt_t.GetProperties():
            try:
                v = getattr(pt, pr.Name)
            except Exception as ex:
                v = "<%s>" % ex.__class__.__name__
            print("     %-20s %s" % (pr.Name, v))
except Exception as ex:
    print("  raised:", ex)

app.CloseApplication()
print()
print("done")
